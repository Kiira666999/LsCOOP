using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopGameplayFileBridge
    {
        private const string BridgeVersion = "1";
        private const string TransportMode = "RAGECOOP";
        private const string OutboundFilePrefix = "LsrCoopGameplayOut.";
        private const string InboundFilePrefix = "LsrCoopGameplayIn.";
        private const string FileExtension = ".txt";
        private static readonly HashSet<string> ProcessedInboundNonces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static DateTimeOffset nextInboundPollUtc = DateTimeOffset.MinValue;

        public static void PublishGameplayCommit(CoopStorePurchaseCommit commit)
        {
            if (!CoopStartupBridge.IsCoopEnabled || commit?.Request == null || commit.Result == null)
            {
                return;
            }

            WriteOutbound("GameplayActionCommitted", commit.Request.WorldId.ToString(), commit.Request.SourceProfileId.ToString(), SimpleJson.Serialize(commit));
        }

        public static void PublishPvpCrimeReport(CoopCrimeEvent crimeEvent)
        {
            if (!CoopStartupBridge.IsCoopEnabled || crimeEvent == null || string.IsNullOrWhiteSpace(crimeEvent.VictimProfileId.ToString()))
            {
                return;
            }

            var dto = new
            {
                EventId = crimeEvent.EventId,
                WorldId = crimeEvent.WorldId.ToString(),
                SourceProfileId = crimeEvent.OffenderProfileId.ToString(),
                SourceCharacterId = crimeEvent.OffenderCharacterId.ToString(),
                TargetProfileId = crimeEvent.VictimProfileId.ToString(),
                TargetCharacterId = crimeEvent.VictimCharacterId.ToString(),
                OffenderPedHandle = crimeEvent.ActorContext?.ActorPedHandle ?? 0,
                VictimPedHandle = crimeEvent.VictimContext?.VictimPedHandle ?? 0,
                crimeEvent.WasKilled,
                crimeEvent.WasShot,
                crimeEvent.WasMeleeAttacked,
                crimeEvent.WasHitByVehicle,
                PositionX = crimeEvent.Position.X,
                PositionY = crimeEvent.Position.Y,
                PositionZ = crimeEvent.Position.Z,
                crimeEvent.TimestampUtc,
            };

            WriteOutbound("PvpCrimeReported", crimeEvent.WorldId.ToString(), crimeEvent.OffenderProfileId.ToString(), SimpleJson.Serialize(dto));
        }

        public static void PublishCriminalJusticeSnapshot(CoopCriminalJusticeStateSnapshot snapshot)
        {
            if (!CoopStartupBridge.IsCoopEnabled || snapshot == null || snapshot.ProfileId.IsEmpty)
            {
                return;
            }

            WriteOutbound("CriminalJusticeSnapshotCommitted", snapshot.WorldId.ToString(), snapshot.ProfileId.ToString(), SimpleJson.Serialize(snapshot));
        }

        public static void PublishGangReputationSnapshot(CoopGangReputationState snapshot)
        {
            if (!CoopStartupBridge.IsCoopEnabled || snapshot == null || snapshot.ProfileId.IsEmpty)
            {
                return;
            }

            WriteOutbound("GangReputationSnapshotCommitted", snapshot.WorldId.ToString(), snapshot.ProfileId.ToString(), SimpleJson.Serialize(snapshot));
        }

        public static void PollInbound()
        {
            if (!CoopStartupBridge.IsCoopEnabled || DateTimeOffset.UtcNow < nextInboundPollUtc)
            {
                return;
            }

            nextInboundPollUtc = DateTimeOffset.UtcNow.AddMilliseconds(500);
            foreach (string path in GetInboundPaths().ToArray())
            {
                TryProcessInbound(path);
            }
        }

        private static void TryProcessInbound(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            Dictionary<string, string> values;
            try
            {
                values = ReadBridgeKeyValues(path);
            }
            catch
            {
                return;
            }

            string nonce = GetValue(values, "Nonce");
            if (string.IsNullOrWhiteSpace(nonce) || ProcessedInboundNonces.Contains(nonce))
            {
                TryDelete(path);
                return;
            }

            if (!IsCurrentProcess(values) || !IsValidBridgeFile(values))
            {
                return;
            }

            string worldId = GetValue(values, "WorldId");
            if (!string.IsNullOrWhiteSpace(worldId) && !string.Equals(worldId, CoopStartupBridge.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool handled = false;
            string eventType = GetValue(values, "EventType");
            if (string.Equals(eventType, "GameplayActionResult", StringComparison.OrdinalIgnoreCase))
            {
                CoopStorePurchaseBridge.HandlePurchaseResult(
                    GetValue(values, "RequestId"),
                    IsTrue(GetValue(values, "Accepted")),
                    IsTrue(GetValue(values, "RequiresResync")),
                    GetValue(values, "Reason"));
                handled = true;
            }
            else if (string.Equals(eventType, "ApplyRemotePvpCrime", StringComparison.OrdinalIgnoreCase))
            {
                handled = CoopCrimeRoutingService.ApplyRemotePlayerOnPlayerViolence(
                    GetValue(values, "SourceProfileId"),
                    GetValue(values, "TargetProfileId"),
                    GetInt(values, "OffenderPedHandle"),
                    GetInt(values, "VictimPedHandle"),
                    IsTrue(GetValue(values, "WasKilled")),
                    IsTrue(GetValue(values, "WasShot")),
                    IsTrue(GetValue(values, "WasMeleeAttacked")),
                    IsTrue(GetValue(values, "WasHitByVehicle")));
            }

            if (handled)
            {
                ProcessedInboundNonces.Add(nonce);
                TryDelete(path);
            }
        }

        private static void WriteOutbound(string eventType, string worldId, string profileId, string payloadJson)
        {
            string nonce = Guid.NewGuid().ToString("N");
            string[] lines =
            {
                $"BridgeVersion={BridgeVersion}",
                $"TransportMode={TransportMode}",
                $"Direction=LSR_TO_RAGECOOP",
                $"ProcessId={Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)}",
                $"EventType={Escape(eventType)}",
                $"WorldId={Escape(worldId)}",
                $"ProfileId={Escape(profileId)}",
                $"TimestampUtc={Escape(DateTime.UtcNow.ToString("O"))}",
                $"Nonce={Escape(nonce)}",
                $"PayloadJson={Escape(payloadJson)}",
            };

            foreach (string folder in GetBridgeFolders())
            {
                WriteAtomic(folder, OutboundFilePrefix + nonce + FileExtension, lines, nonce);
            }
        }

        private static IEnumerable<string> GetInboundPaths()
        {
            foreach (string folder in GetBridgeFolders())
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string path in Directory.GetFiles(folder, InboundFilePrefix + "*" + FileExtension))
                {
                    yield return path;
                }
            }
        }

        private static bool IsCurrentProcess(Dictionary<string, string> values)
        {
            int processId;
            return int.TryParse(GetValue(values, "ProcessId"), out processId) && processId == Process.GetCurrentProcess().Id;
        }

        private static bool IsValidBridgeFile(Dictionary<string, string> values)
        {
            return string.Equals(GetValue(values, "BridgeVersion"), BridgeVersion, StringComparison.Ordinal)
                && string.Equals(GetValue(values, "TransportMode"), TransportMode, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> ReadBridgeKeyValues(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int separatorIndex = line?.IndexOf('=') ?? -1;
                if (separatorIndex <= 0)
                {
                    continue;
                }

                values[line.Substring(0, separatorIndex)] = Uri.UnescapeDataString(line.Substring(separatorIndex + 1) ?? string.Empty);
            }

            return values;
        }

        private static string GetValue(Dictionary<string, string> values, string key)
        {
            string value;
            return values != null && values.TryGetValue(key, out value) ? value ?? string.Empty : string.Empty;
        }

        private static int GetInt(Dictionary<string, string> values, string key)
        {
            int parsed;
            return int.TryParse(GetValue(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteAtomic(string folder, string fileName, string[] lines, string nonce)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                string targetPath = Path.Combine(folder, fileName);
                string tempPath = targetPath + "." + nonce + ".tmp";
                File.WriteAllLines(tempPath, lines);
                File.Move(tempPath, targetPath);
            }
            catch
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        private static string[] GetBridgeFolders()
        {
            return new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED"),
                Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "LosSantosRED"),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static class SimpleJson
        {
            public static string Serialize(object value)
            {
                StringBuilder builder = new StringBuilder();
                WriteValue(builder, value);
                return builder.ToString();
            }

            private static void WriteValue(StringBuilder builder, object value)
            {
                if (value == null)
                {
                    builder.Append("null");
                    return;
                }

                Type type = value.GetType();
                if (value is string || value is CoopWorldId || value is CoopProfileId || value is CoopCharacterId || type.IsEnum)
                {
                    WriteString(builder, value.ToString());
                    return;
                }

                if (value is bool boolValue)
                {
                    builder.Append(boolValue ? "true" : "false");
                    return;
                }

                if (value is DateTimeOffset dateTimeOffset)
                {
                    WriteString(builder, dateTimeOffset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    return;
                }

                if (value is DateTime dateTime)
                {
                    WriteString(builder, dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                    return;
                }

                if (IsNumber(value))
                {
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }

                if (value is IDictionary dictionary)
                {
                    WriteDictionary(builder, dictionary);
                    return;
                }

                if (value is IEnumerable enumerable)
                {
                    WriteEnumerable(builder, enumerable);
                    return;
                }

                WriteObject(builder, value);
            }

            private static void WriteDictionary(StringBuilder builder, IDictionary dictionary)
            {
                builder.Append('{');
                bool needsComma = false;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (needsComma)
                    {
                        builder.Append(',');
                    }

                    WriteString(builder, entry.Key?.ToString() ?? string.Empty);
                    builder.Append(':');
                    WriteValue(builder, entry.Value);
                    needsComma = true;
                }

                builder.Append('}');
            }

            private static void WriteEnumerable(StringBuilder builder, IEnumerable enumerable)
            {
                builder.Append('[');
                bool needsComma = false;
                foreach (object item in enumerable)
                {
                    if (needsComma)
                    {
                        builder.Append(',');
                    }

                    WriteValue(builder, item);
                    needsComma = true;
                }

                builder.Append(']');
            }

            private static void WriteObject(StringBuilder builder, object value)
            {
                builder.Append('{');
                bool needsComma = false;
                foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.GetIndexParameters().Length == 0))
                {
                    if (needsComma)
                    {
                        builder.Append(',');
                    }

                    WriteString(builder, property.Name);
                    builder.Append(':');
                    WriteValue(builder, property.GetValue(value, null));
                    needsComma = true;
                }

                builder.Append('}');
            }

            private static void WriteString(StringBuilder builder, string value)
            {
                builder.Append('"');
                foreach (char c in value ?? string.Empty)
                {
                    switch (c)
                    {
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(c);
                            }
                            break;
                    }
                }

                builder.Append('"');
            }

            private static bool IsNumber(object value)
            {
                return value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint
                    || value is long || value is ulong || value is float || value is double || value is decimal;
            }
        }
    }
}
