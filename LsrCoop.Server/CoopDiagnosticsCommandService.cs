using RageCoop.Server;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LsrCoop.Server
{
    internal sealed class CoopDiagnosticsCommandService
    {
        private const int MaxRows = 10;

        private readonly WorldProfileStoreService worldProfileStoreService;
        private readonly PlayerRegistrationService playerRegistrationService;
        private readonly RoleConfigService roleConfigService;
        private readonly ActiveHostService activeHostService;
        private readonly Func<string> lastHandoffReasonProvider;
        private readonly Action<string> info;
        private readonly Action<string> warning;
        private bool chatReplyUnavailableLogged;

        public CoopDiagnosticsCommandService(
            WorldProfileStoreService worldProfileStoreService,
            PlayerRegistrationService playerRegistrationService,
            RoleConfigService roleConfigService,
            ActiveHostService activeHostService,
            Func<string> lastHandoffReasonProvider,
            Action<string> info,
            Action<string> warning)
        {
            this.worldProfileStoreService = worldProfileStoreService;
            this.playerRegistrationService = playerRegistrationService;
            this.roleConfigService = roleConfigService;
            this.activeHostService = activeHostService;
            this.lastHandoffReasonProvider = lastHandoffReasonProvider;
            this.info = info;
            this.warning = warning;
        }

        public bool TryHandleRegisteredCommand(object commandContext)
        {
            return TryHandleCoopArgs(GetClient(commandContext), GetArgs(commandContext));
        }

        public bool TryHandleCommandEvent(object eventPayload)
        {
            string commandName = GetStringProperty(eventPayload, "Name");
            if (!IsCoopCommand(commandName))
            {
                return false;
            }

            bool handled = TryHandleCoopArgs(GetClient(eventPayload), GetArgs(eventPayload));
            SetBooleanProperty(eventPayload, "Cancel", true);
            return handled;
        }

        public bool TryHandleChatMessage(object eventPayload)
        {
            string message = GetStringProperty(eventPayload, "Message");
            List<string> tokens = Tokenize(message);
            if (tokens.Count == 0 || !IsCoopCommand(tokens[0]))
            {
                return false;
            }

            return TryHandleCoopArgs(GetClient(eventPayload), tokens.Skip(1).ToList());
        }

        private bool TryHandleCoopArgs(Client client, IReadOnlyList<string> args)
        {
            try
            {
                List<string> tokens = args?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? new List<string>();
                string senderProfileId = playerRegistrationService.GetClientProfileId(client);
                if (string.IsNullOrWhiteSpace(senderProfileId) || !roleConfigService.IsAdmin(senderProfileId))
                {
                    Reply(client, "[LsrCoop.Server] admin-only: /coop diagnostics require an Admin profile");
                    return true;
                }

                if (tokens.Count == 0)
                {
                    ReplyUsage(client);
                    return true;
                }

                string subCommand = tokens[0].ToLowerInvariant();
                switch (subCommand)
                {
                    case "status":
                        if (tokens.Count != 1)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop status");
                            return true;
                        }

                        ReplyStatus(client);
                        return true;

                    case "profile":
                        if (tokens.Count > 2)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop profile [profileId]");
                            return true;
                        }

                        ReplyProfile(client, tokens.Count == 2 ? tokens[1] : senderProfileId);
                        return true;

                    case "vehicles":
                        if (tokens.Count != 2)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop vehicles <profileId>");
                            return true;
                        }

                        ReplyVehicles(client, tokens[1]);
                        return true;

                    case "properties":
                        if (tokens.Count != 2)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop properties <profileId>");
                            return true;
                        }

                        ReplyProperties(client, tokens[1]);
                        return true;

                    case "criminal":
                        if (tokens.Count != 2)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop criminal <profileId>");
                            return true;
                        }

                        ReplyCriminalHistory(client, tokens[1]);
                        return true;

                    case "gangs":
                        if (tokens.Count != 2)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop gangs <profileId>");
                            return true;
                        }

                        ReplyGangs(client, tokens[1]);
                        return true;

                    case "gang":
                        if (tokens.Count != 3)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop gang <profileId> <gangIdOrName>");
                            return true;
                        }

                        ReplyGang(client, tokens[1], tokens[2]);
                        return true;

                    case "last":
                        if (tokens.Count != 2)
                        {
                            Reply(client, "[LsrCoop.Server] usage: /coop last <profileId>");
                            return true;
                        }

                        ReplyLast(client, tokens[1]);
                        return true;

                    default:
                        ReplyUsage(client);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Reply(client, $"[LsrCoop.Server] diagnostics command failed: {Clean(ex.Message)}");
                return true;
            }
        }

        private void ReplyStatus(Client client)
        {
            string activeHost = string.IsNullOrWhiteSpace(activeHostService.ActiveHostId) ? "none" : activeHostService.ActiveHostId;
            string lastHandoffReason = SafeGetLastHandoffReason();
            Reply(client, "[LsrCoop.Server] "
                + $"world={Clean(worldProfileStoreService.WorldId)} "
                + $"profiles={worldProfileStoreService.Profiles?.Count ?? 0} "
                + $"connectedClients={playerRegistrationService.ConnectedClients.Count()} "
                + $"activeHost={Clean(activeHost)} "
                + $"trustedHosts={DistinctCount(roleConfigService.Config?.TrustedHostIds)} "
                + $"admins={DistinctCount(roleConfigService.Config?.AdminIds)} "
                + $"lastHandoffReason={Clean(lastHandoffReason, "none")}");
        }

        private void ReplyProfile(Client client, string profileId)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            CoopClientStatus status = GetStatus(profile.ProfileId);
            CoopInventoryMoneySnapshot money = profile.InventoryMoney;
            CoopCriminalHistoryStateDto criminalHistory = profile.CriminalHistory;
            List<CoopGangReputationRecordDto> nonDefaultGangs = GetNonDefaultGangRecords(profile.GangReputation).Take(4).ToList();
            string gangSummary = nonDefaultGangs.Count > 0 && nonDefaultGangs.Count <= 3
                ? " nonDefaultGangs=" + string.Join(",", nonDefaultGangs.Select(x => $"{CleanGangLabel(x)}:{x.Reputation}"))
                : string.Empty;

            Reply(client, "[LsrCoop.Server] "
                + $"profile={Clean(profile.ProfileId)} "
                + $"role={Clean(roleConfigService.GetRoleName(profile.ProfileId))} "
                + $"connected={(status?.Client != null)} "
                + $"compatibility={Clean(status?.CompatibilityState.ToString() ?? "Unknown")} "
                + $"readiness={Clean(status?.ReadinessState.ToString() ?? "Disconnected")} "
                + $"money={money?.TotalMoney ?? 0} "
                + $"onHand={money?.OnHandCash ?? 0} "
                + $"inventory={money?.InventoryItems?.Count ?? 0} "
                + $"weapons={profile.Weapons?.Weapons?.Count ?? 0} "
                + $"vehicles={profile.OwnedVehicles?.Vehicles?.Count ?? 0} "
                + $"properties={profile.PropertyOwnership?.Properties?.Count ?? 0} "
                + $"criminalHistory={criminalHistory?.Crimes?.Count ?? 0} "
                + $"maxWanted={GetMaxWantedLevel(criminalHistory)} "
                + $"clearReason={Clean(criminalHistory?.ClearReason, "none")} "
                + $"gangReputation={profile.GangReputation?.Reputations?.Count ?? 0}"
                + gangSummary);
        }

        private void ReplyVehicles(Client client, string profileId)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            List<CoopOwnedVehicleRecord> vehicles = profile.OwnedVehicles?.Vehicles?.Where(x => x != null).ToList() ?? new List<CoopOwnedVehicleRecord>();
            List<string> lines = new List<string> { $"[LsrCoop.Server] vehicles for {Clean(profile.ProfileId)}: count={vehicles.Count}" };
            int index = 1;
            foreach (CoopOwnedVehicleRecord vehicle in vehicles.Take(MaxRows))
            {
                string id = string.IsNullOrWhiteSpace(vehicle.VehicleId) ? string.Empty : $" id={Clean(vehicle.VehicleId)}";
                lines.Add($"{index}. model={Clean(FirstNonEmpty(vehicle.ModelName, vehicle.ModelHash))} plate={Clean(vehicle.PlateNumber, "none")}{id}");
                index++;
            }

            AddTruncatedLine(lines, vehicles.Count);
            ReplyLines(client, lines);
        }

        private void ReplyProperties(Client client, string profileId)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            List<CoopPropertyOwnershipRecord> properties = profile.PropertyOwnership?.Properties?.Where(x => x != null).ToList() ?? new List<CoopPropertyOwnershipRecord>();
            List<string> lines = new List<string> { $"[LsrCoop.Server] properties for {Clean(profile.ProfileId)}: count={properties.Count}" };
            int index = 1;
            foreach (CoopPropertyOwnershipRecord property in properties.Take(MaxRows))
            {
                lines.Add($"{index}. {Clean(property.PropertyType, "Property")} | {Clean(property.Name)} | owned={property.IsOwned} | rented={property.IsRented} | rentedOut={property.IsRentedOut}");
                index++;
            }

            AddTruncatedLine(lines, properties.Count);
            ReplyLines(client, lines);
        }

        private void ReplyCriminalHistory(Client client, string profileId)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            CoopCriminalHistoryStateDto history = profile.CriminalHistory;
            Reply(client, "[LsrCoop.Server] "
                + $"criminal history for {Clean(profile.ProfileId)}: "
                + $"crimes={history?.Crimes?.Count ?? 0} "
                + $"maxWanted={GetMaxWantedLevel(history)} "
                + $"clearReason={Clean(history?.ClearReason, "none")} "
                + $"dateTimeLastWantedEnded={FormatUtc(history?.DateTimeLastWantedEnded)} "
                + "temporaryStatePersisted=False");
        }

        private void ReplyGangs(Client client, string profileId)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            List<CoopGangReputationRecordDto> records = profile.GangReputation?.Reputations?.Where(x => x != null).ToList() ?? new List<CoopGangReputationRecordDto>();
            List<CoopGangReputationRecordDto> nonDefault = GetNonDefaultGangRecords(profile.GangReputation).ToList();
            List<string> lines = new List<string> { $"[LsrCoop.Server] gang reputation for {Clean(profile.ProfileId)}: records={records.Count} nonDefault={nonDefault.Count}" };
            foreach (CoopGangReputationRecordDto record in nonDefault.Take(MaxRows))
            {
                lines.Add($"{FormatGangName(record)}: rep={record.Reputation} hurt={record.MembersHurt} killed={record.MembersKilled}");
            }

            if (nonDefault.Count == 0)
            {
                lines.Add("no non-default gang records");
            }

            AddTruncatedLine(lines, nonDefault.Count);
            ReplyLines(client, lines);
        }

        private void ReplyGang(Client client, string profileId, string gangIdOrName)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            CoopGangReputationRecordDto record = FindGangRecord(profile.GangReputation, gangIdOrName);
            if (record == null)
            {
                Reply(client, $"[LsrCoop.Server] gang record not found: profile={Clean(profile.ProfileId)} gang={Clean(gangIdOrName)}");
                return;
            }

            Reply(client, "[LsrCoop.Server] "
                + $"gang reputation for {Clean(profile.ProfileId)}: "
                + $"{FormatGangName(record)} "
                + $"rep={record.Reputation} "
                + $"hurt={record.MembersHurt} "
                + $"killed={record.MembersKilled} "
                + $"carjacked={record.MembersCarJacked} "
                + $"hurtInTerritory={record.MembersHurtInTerritory} "
                + $"killedInTerritory={record.MembersKilledInTerritory} "
                + $"carjackedInTerritory={record.MembersCarJackedInTerritory} "
                + $"debt={record.PlayerDebt} "
                + $"member={record.IsMember} "
                + $"enemy={record.IsEnemy} "
                + $"tasksCompleted={record.TasksCompleted}");
        }

        private void ReplyLast(Client client, string profileId)
        {
            CoopPlayerProfile profile = GetProfileOrReply(client, profileId);
            if (profile == null)
            {
                return;
            }

            Reply(client, $"[LsrCoop.Server] last persistence summary for {Clean(profile.ProfileId)} is not tracked yet; follow-up needed for /coop last");
        }

        private CoopPlayerProfile GetProfileOrReply(Client client, string profileId)
        {
            CoopPlayerProfile profile = worldProfileStoreService.GetProfile(profileId);
            if (profile == null)
            {
                Reply(client, $"[LsrCoop.Server] profile not found: {Clean(profileId)}");
            }

            return profile;
        }

        private CoopClientStatus GetStatus(string profileId)
        {
            return playerRegistrationService.ClientStatuses.TryGetValue(profileId ?? string.Empty, out CoopClientStatus status) ? status : null;
        }

        private int GetMaxWantedLevel(CoopCriminalHistoryStateDto history)
        {
            int maxCrimeWanted = history?.Crimes?.Count > 0 ? history.Crimes.Max(x => x?.ResultingWantedLevel ?? 0) : 0;
            return Math.Max(history?.WantedLevel ?? 0, maxCrimeWanted);
        }

        private IEnumerable<CoopGangReputationRecordDto> GetNonDefaultGangRecords(CoopGangReputationStateDto state)
        {
            return state?.Reputations?
                .Where(IsNonDefaultGangRecord)
                .OrderBy(x => x.GangName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.GangId, StringComparer.OrdinalIgnoreCase)
                ?? Enumerable.Empty<CoopGangReputationRecordDto>();
        }

        private bool IsNonDefaultGangRecord(CoopGangReputationRecordDto record)
        {
            return record != null
                && (record.Reputation != 0
                    || record.MembersHurt != 0
                    || record.MembersKilled != 0
                    || record.MembersCarJacked != 0
                    || record.MembersHurtInTerritory != 0
                    || record.MembersKilledInTerritory != 0
                    || record.MembersCarJackedInTerritory != 0
                    || record.PlayerDebt != 0
                    || record.IsMember
                    || record.IsEnemy
                    || record.TasksCompleted != 0);
        }

        private CoopGangReputationRecordDto FindGangRecord(CoopGangReputationStateDto state, string gangIdOrName)
        {
            string search = gangIdOrName ?? string.Empty;
            List<CoopGangReputationRecordDto> records = state?.Reputations?.Where(x => x != null).ToList() ?? new List<CoopGangReputationRecordDto>();
            return records.FirstOrDefault(x => string.Equals(x.GangId, search, StringComparison.OrdinalIgnoreCase))
                ?? records.FirstOrDefault(x => string.Equals(x.GangName, search, StringComparison.OrdinalIgnoreCase))
                ?? records.FirstOrDefault(x => ContainsIgnoreCase(x.GangId, search) || ContainsIgnoreCase(x.GangName, search));
        }

        private bool ContainsIgnoreCase(string value, string search)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(search)
                && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddTruncatedLine(List<string> lines, int totalCount)
        {
            if (totalCount > MaxRows)
            {
                lines.Add($"... showing {MaxRows} of {totalCount}");
            }
        }

        private void ReplyUsage(Client client)
        {
            Reply(client, "[LsrCoop.Server] usage: /coop status | profile [profileId] | vehicles <profileId> | properties <profileId> | criminal <profileId> | gangs <profileId> | gang <profileId> <gangIdOrName> | last <profileId>");
        }

        private void ReplyLines(Client client, IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                Reply(client, line);
            }
        }

        private void Reply(Client client, string message)
        {
            string line = string.IsNullOrWhiteSpace(message) ? "[LsrCoop.Server]" : message;
            info?.Invoke(line);
            TrySendChatReply(client, line);
        }

        private void TrySendChatReply(Client client, string line)
        {
            if (client == null)
            {
                return;
            }

            try
            {
                MethodInfo sendChatMessage = client.GetType().GetMethod("SendChatMessage", new[] { typeof(string), typeof(string) });
                if (sendChatMessage == null)
                {
                    return;
                }

                sendChatMessage.Invoke(client, new object[] { line, "LsrCoop.Server" });
            }
            catch (Exception ex)
            {
                if (!chatReplyUnavailableLogged)
                {
                    chatReplyUnavailableLogged = true;
                    warning?.Invoke($"[LsrCoop.Server] chat diagnostics reply unavailable: {ex.Message}");
                }
            }
        }

        private Client GetClient(object source)
        {
            return GetProperty(source, "Client") as Client;
        }

        private IReadOnlyList<string> GetArgs(object source)
        {
            object args = GetProperty(source, "Args");
            if (args == null)
            {
                return new List<string>();
            }

            if (args is string singleString)
            {
                return Tokenize(singleString);
            }

            if (args is IEnumerable<string> stringEnumerable)
            {
                return stringEnumerable.ToList();
            }

            if (args is IEnumerable enumerable)
            {
                List<string> values = new List<string>();
                foreach (object value in enumerable)
                {
                    values.Add(value?.ToString() ?? string.Empty);
                }

                return values;
            }

            return new List<string>();
        }

        private object GetProperty(object source, string propertyName)
        {
            return source?.GetType().GetProperty(propertyName)?.GetValue(source);
        }

        private string GetStringProperty(object source, string propertyName)
        {
            return GetProperty(source, propertyName)?.ToString() ?? string.Empty;
        }

        private void SetBooleanProperty(object source, string propertyName, bool value)
        {
            try
            {
                PropertyInfo property = source?.GetType().GetProperty(propertyName);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                {
                    property.SetValue(source, value);
                }
            }
            catch
            {
            }
        }

        private bool IsCoopCommand(string token)
        {
            string normalized = (token ?? string.Empty).Trim().TrimStart('/');
            return string.Equals(normalized, "coop", StringComparison.OrdinalIgnoreCase);
        }

        private List<string> Tokenize(string commandLine)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return tokens;
            }

            StringBuilder current = new StringBuilder();
            bool inQuote = false;
            foreach (char character in commandLine)
            {
                if (character == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (char.IsWhiteSpace(character) && !inQuote)
                {
                    AddToken(tokens, current);
                    continue;
                }

                current.Append(character);
            }

            AddToken(tokens, current);
            return tokens;
        }

        private void AddToken(List<string> tokens, StringBuilder current)
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }

        private string SafeGetLastHandoffReason()
        {
            try
            {
                return lastHandoffReasonProvider?.Invoke();
            }
            catch
            {
                return string.Empty;
            }
        }

        private int DistinctCount(IEnumerable<string> values)
        {
            return values?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0;
        }

        private string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }

        private string FormatGangName(CoopGangReputationRecordDto record)
        {
            string name = Clean(record?.GangName, string.Empty);
            string id = Clean(record?.GangId, string.Empty);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
            {
                return $"{name} / {id}";
            }

            return Clean(FirstNonEmpty(name, id), "unknown");
        }

        private string CleanGangLabel(CoopGangReputationRecordDto record)
        {
            return Clean(FirstNonEmpty(record?.GangName, record?.GangId), "unknown");
        }

        private string FormatUtc(DateTimeOffset? value)
        {
            if (!value.HasValue || value.Value == default)
            {
                return "none";
            }

            return value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private string Clean(string value, string fallback = "none")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
