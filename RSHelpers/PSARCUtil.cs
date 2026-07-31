using Rocksmith2014PsarcLib.Psarc;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using RockSnifferLib.Logging;
using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RockSnifferLib.RSHelpers
{
    public static class PSARCUtil
    {
        internal readonly record struct PSARCFileSnapshot(long Length, DateTime LastWriteTimeUtc);

        private const int PSARCHeaderLength = 32;
        private const int PSARCReadyAttempts = 40;
        private const int PSARCReadyDelayMilliseconds = 250;
        private const int PSARCStableObservations = 4;

        internal static FileStream OpenReadShared(FileInfo fileInfo)
        {
            return new FileStream(
                fileInfo.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
        }

        /// <summary>
        /// Waits for a file to exist and be available for reading
        /// </summary>
        /// <param name="fileInfo"></param>
        private static void WaitForFile(FileInfo fileInfo)
        {
            //Check that the file exists, just in case
            if (!fileInfo.Exists)
            {
                //If it doesn't exist, wait for a bit to see if it magically starts existing
                //If you download the file directly from your browser, it might not exist
                //immediately (though we get the notification about it early?)
                for (int tries = 0; tries < 10; tries++)
                {
                    Thread.Sleep(1000);
                    fileInfo.Refresh();
                    if (fileInfo.Exists) break;
                }
            }

            //Try to open the file for reading, to detect if we are able to read it
            for (int tries = 0; tries < 10; tries++)
            {
                try
                {
                    using (FileStream stream = OpenReadShared(fileInfo))
                    {
                        break; // break when file was successfully opened
                    }
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }

        internal static bool TryWaitForStablePSARC(
            FileInfo fileInfo,
            out PSARCFileSnapshot snapshot,
            int maxAttempts = PSARCReadyAttempts,
            int delayMilliseconds = PSARCReadyDelayMilliseconds,
            int requiredStableObservations = PSARCStableObservations,
            CancellationToken cancellationToken = default
        )
        {
            if (maxAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            if (delayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
            }

            if (requiredStableObservations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredStableObservations));
            }

            snapshot = default;
            PSARCFileSnapshot? previousSnapshot = null;
            var stableObservations = 0;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (TryGetPSARCFileSnapshot(fileInfo, out var currentSnapshot))
                {
                    stableObservations =
                        previousSnapshot.HasValue &&
                        previousSnapshot.Value.Equals(currentSnapshot)
                            ? stableObservations + 1
                            : 1;
                    previousSnapshot = currentSnapshot;

                    if (stableObservations >= requiredStableObservations)
                    {
                        snapshot = currentSnapshot;
                        return true;
                    }
                }
                else
                {
                    previousSnapshot = null;
                    stableObservations = 0;
                }

                if (attempt + 1 < maxAttempts && delayMilliseconds > 0)
                {
                    if (cancellationToken.WaitHandle.WaitOne(delayMilliseconds))
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        internal static bool MatchesPSARCFileSnapshot(
            FileInfo fileInfo,
            PSARCFileSnapshot expectedSnapshot
        )
        {
            return TryGetPSARCFileSnapshot(fileInfo, out var currentSnapshot) &&
                currentSnapshot.Equals(expectedSnapshot);
        }

        internal static bool TryGetPSARCFileSnapshot(
            FileInfo fileInfo,
            out PSARCFileSnapshot snapshot
        )
        {
            snapshot = default;

            try
            {
                fileInfo.Refresh();
                if (!fileInfo.Exists || fileInfo.Length < PSARCHeaderLength)
                {
                    return false;
                }

                var initialLength = fileInfo.Length;
                var initialLastWriteTimeUtc = fileInfo.LastWriteTimeUtc;

                using (var stream = OpenReadShared(fileInfo))
                {
                    if (stream.Length != initialLength)
                    {
                        return false;
                    }

                    Span<byte> identifier = stackalloc byte[4];
                    if (
                        stream.Read(identifier) != identifier.Length ||
                        identifier[0] != (byte)'P' ||
                        identifier[1] != (byte)'S' ||
                        identifier[2] != (byte)'A' ||
                        identifier[3] != (byte)'R'
                    )
                    {
                        return false;
                    }
                }

                fileInfo.Refresh();
                if (
                    !fileInfo.Exists ||
                    fileInfo.Length != initialLength ||
                    fileInfo.LastWriteTimeUtc != initialLastWriteTimeUtc
                )
                {
                    return false;
                }

                snapshot = new PSARCFileSnapshot(initialLength, initialLastWriteTimeUtc);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads psarc file from filepath and populates details with information
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="fileHash"></param>
        internal static Dictionary<string, SongDetails> ReadPSARCHeaderData(FileInfo fileInfo, string fileHash)
        {
            //Wait for the file to exist
            WaitForFile(fileInfo);

            if (!fileInfo.Exists)
            {
                Logger.LogError("Warning! Psarc file {0} does not exist!", fileInfo.FullName);
                return null;
            }

            var sw = new Stopwatch();
            sw.Start();

            var detailsDict = new Dictionary<string, SongDetails>();

            using (PsarcFile loader = new PsarcFile(fileInfo))
            {
                //Extract toolkit info
                var tkInfo = loader.ExtractToolkitInfo();

                List<SongArrangement> manifests;

                try
                {
                    manifests = loader.ExtractArrangementManifests();
                }
                catch (Exception e)
                {
                    Logger.LogError("Warning! Could not parse psarc file {0}: {1}", fileInfo.Name, e.Message);
                    return null;
                }
                
                //Extract all arrangements
                foreach (var v in manifests)
                {
                    if (v == null)
                    {
                        Logger.LogError("Unable to process JSON manifest for {0}", fileInfo.Name);
                        continue;
                    }

                    var arrangement = v.Attributes;
                    var arrangement_id = arrangement.PersistentID;
                    var arrangement_name = arrangement.ArrangementName;

                    if (arrangement_name.Equals("Vocals"))
                    {
                        if (!detailsDict.ContainsKey(arrangement.SongKey))
                        {
                            detailsDict[arrangement.SongKey] = new SongDetails();
                        }

                        SongDetails details = detailsDict[arrangement.SongKey];

                        var arrangementSng = loader.InflateEntry<SngAsset>(a => a.Path.Equals($"songs/bin/generic/{arrangement.SongXml.Substring(20)}.sng"));
                        var arrangement_vocals = arrangementSng.Vocals;

                        //Get a list of all vocals
                        List<SongDetails.VocalDetails> vocals = [];
                        if (arrangement_vocals != null && details.vocals != null && details.vocals.Count == 0)
                        {
                            foreach (var voc in arrangement_vocals)
                            {
                                var vocalDetails = new SongDetails.VocalDetails
                                {
                                    Time = voc.Time,
                                    Note = voc.Note,
                                    Length = voc.Length,
                                    Lyric = voc.Lyric
                                };
                                vocals.Add(vocalDetails);
                            }
                            details.vocals = vocals;
                        }
                    }
                    else //Not a vocal arrangement
                    {
                        var sngPath = $"songs/bin/generic/{arrangement.SongXml.Substring(20)}.sng";
                        var arrangementSng = loader.InflateEntry<SngAsset>(a => a.Path.Equals(sngPath));
                        ArrangementData arrangementData = new(arrangementSng);

                        if (arrangement.Phrases != null)
                        {
                            if (!detailsDict.TryGetValue(arrangement.SongKey, out SongDetails? details))
                            {
                                details = new SongDetails();
                                detailsDict[arrangement.SongKey] = details;
                            }

                            if (details.albumArt == null)
                            {
                                try
                                {
                                    details.albumArt = loader.ExtractAlbumArt(arrangement).Bitmap;
                                }
                                catch (Exception e)
                                {
                                    Logger.LogError("Warning: couldn't extract album art for {0}", arrangement.SongName);
#if DEBUG
                                    Logger.LogException(e);
#endif

                                    details.albumArt = new Bitmap(1, 1);
                                }
                            }

                            //Get a list of all sections
                            List<ArrangementDetails.SectionDetails> sections = [];
                            Dictionary<string, int> sectionCounts = [];

                            foreach (var sect in arrangement.Sections)
                            {
                                if (!sectionCounts.ContainsKey(sect.Name))
                                {
                                    sectionCounts[sect.Name] = 1;
                                }

                                var sectionDetails = new ArrangementDetails.SectionDetails
                                {
                                    name = $"{sect.Name} {sectionCounts[sect.Name]}",
                                    startTime = sect.StartTime,
                                    endTime = sect.EndTime
                                };

                                sections.Add(sectionDetails);
                                sectionCounts[sect.Name]++;
                            }


                            //Get a list of all phraseIterations
                            List<ArrangementDetails.PhraseIterationDetails> phraseIterations = [];
                            Dictionary<string, int> phraseIterationCounts = [];

                            foreach (var phrI in arrangement.PhraseIterations)
                            {
                                if (!phraseIterationCounts.ContainsKey(phrI.Name))
                                {
                                    phraseIterationCounts[phrI.Name] = 1;
                                }

                                var phraseIterationDetails = new ArrangementDetails.PhraseIterationDetails
                                {
                                    name = $"{phrI.Name} {phraseIterationCounts[phrI.Name]}",
                                    phraseId = phrI.PhraseIndex,
                                    maxDifficulty = phrI.MaxDifficulty,
                                    startTime = phrI.StartTime,
                                    endTime = phrI.EndTime
                                };

                                phraseIterations.Add(phraseIterationDetails);
                                phraseIterationCounts[phrI.Name]++;
                            }

                            //Build arrangement details
                            ArrangementDetails arrangementDetails = new()
                            {
                                name = arrangement.ArrangementName,
                                arrangementID = arrangement_id,
                                songOffset = arrangement.SongOffset,
                                sections = sections,
                                phraseIterations = phraseIterations,
                                data = arrangementData,
                                isBonusArrangement = (arrangement.ArrangementProperties.BonusArr == 1),
                                isAlternateArrangement = (arrangement.ArrangementProperties.Represent == 0)
                            };

                        //Determine path type
                        if (arrangement.ArrangementProperties.PathLead == 1)
                        {
                            arrangementDetails.type = "Lead";
                        }
                        else if (arrangement.ArrangementProperties.PathRhythm == 1)
                        {
                            arrangementDetails.type = "Rhythm";
                        }
                        else if (arrangement.ArrangementProperties.PathBass == 1)
                        {
                            arrangementDetails.type = "Bass";
                        }

                            arrangementDetails.tuning = new ArrangementTuning(arrangement.Tuning, (int)arrangement.CentOffset, (int)arrangement.CapoFret);
                            arrangementDetails.tuning.IsBass = (arrangementDetails.type == "Bass");


                            //file hash
                            details.psarcFileHash = fileHash;

                            //Get general song information
                            details.songID = arrangement.SongKey;
                            details.songLength = arrangement.SongLength;
                            details.songName = arrangement.SongName;
                            details.artistName = arrangement.ArtistName;
                            details.albumName = arrangement.AlbumName;
                            details.albumYear = arrangement.SongYear;
                            details.arrangements.Add(arrangementDetails);

                            //Apply toolkit information
                            details.toolkit = new ToolkitDetails
                            {
                                version = tkInfo.PackageVersion,
                                author = tkInfo.PackageAuthor,
                                comment = tkInfo.PackageComment,
                                package_version = tkInfo.PackageVersion
                            };
                        }
                    }
                }
                sw.Stop();

                Logger.Log("Parsed {0} ({1}mb) in {2}ms and found {3} songs", fileInfo.Name, fileInfo.Length / 1024 / 1024, sw.ElapsedMilliseconds, detailsDict.Count);

                return detailsDict;
            }

        }

        public static string GetFileHash(FileInfo fileInfo)
        {
            WaitForFile(fileInfo);

            //Calculate file hash
            using (var md5 = MD5.Create())
            {
                using (var stream = OpenReadShared(fileInfo))
                {
                    var hash = Convert.ToBase64String(md5.ComputeHash(stream));
                    return hash;
                }
            }

        }
    }
}
