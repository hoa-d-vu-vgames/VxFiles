// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Persists package trust and shared tool configuration per Automation Package, typed settings per
/// Automation Action, and a bounded run history. Selected file contents and full action output are never stored.
/// </summary>
public sealed class FileAutomationStateStore : IAutomationStateStore
{
	private readonly string _stateRoot;
	private readonly TimeSpan _runRecordLifetime;
	private readonly long _runRecordSizeBudget;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public FileAutomationStateStore(string stateRoot)
		: this(stateRoot, TimeSpan.FromDays(7), 100L * 1024 * 1024)
	{
	}

	internal FileAutomationStateStore(string stateRoot, TimeSpan runRecordLifetime, long runRecordSizeBudget)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(runRecordLifetime, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(runRecordSizeBudget, 0);
		_stateRoot = Path.GetFullPath(stateRoot);
		_runRecordLifetime = runRecordLifetime;
		_runRecordSizeBudget = runRecordSizeBudget;
	}

	public async ValueTask<AutomationPackageState> ReadPackageStateAsync(
		AutomationPackageId packageId,
		CancellationToken cancellationToken = default)
	{
		var path = GetPackageStatePath(packageId);
		if (!File.Exists(path))
			return EmptyPackageState();

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var state = await ReadStoredPackageStateWithoutLockAsync(path, cancellationToken);
			return new(state.TrustedFingerprint, state.ExternalTools);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask WritePackageTrustAsync(
		AutomationPackageId packageId,
		string fingerprint,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var path = GetPackageStatePath(packageId);
			var state = File.Exists(path)
				? await ReadStoredPackageStateWithoutLockAsync(path, cancellationToken)
				: EmptyStoredPackageState();
			await WriteJsonAtomicallyAsync(
				path,
				writer => WriteStoredPackageState(writer, state with { TrustedFingerprint = fingerprint }),
				cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<AutomationActionSettings> ReadActionSettingsAsync(
		AutomationActionId actionId,
		CancellationToken cancellationToken = default)
	{
		var path = GetPackageStatePath(actionId.PackageId);
		if (!File.Exists(path))
			return EmptyActionSettings();

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var state = await ReadStoredPackageStateWithoutLockAsync(path, cancellationToken);
			return state.ActionSettings.GetValueOrDefault(actionId.LocalId, EmptyActionSettings());
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask WritePackageConfigurationAsync(
		AutomationPackageId packageId,
		ImmutableDictionary<string, AutomationExternalToolConfiguration> externalTools,
		ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings> actionSettings,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(externalTools);
		ArgumentNullException.ThrowIfNull(actionSettings);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var path = GetPackageStatePath(packageId);
			var state = File.Exists(path)
				? await ReadStoredPackageStateWithoutLockAsync(path, cancellationToken)
				: EmptyStoredPackageState();
			var mergedActionSettings = state.ActionSettings.SetItems(actionSettings);

			await WriteJsonAtomicallyAsync(
				path,
				writer => WriteStoredPackageState(
					writer,
					state with { ExternalTools = externalTools, ActionSettings = mergedActionSettings }),
				cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask AppendRunRecordAsync(
		AutomationRunRecord record,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(record);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var directory = Path.Join(_stateRoot, "runs");
			Directory.CreateDirectory(directory);
			var timestamp = record.Snapshot.CompletedAtUtc ?? record.Snapshot.StartedAtUtc;
			var path = Path.Join(directory, $"{timestamp.UtcTicks:D19}_{record.Snapshot.Id.Value:N}.json");
			await WriteJsonAtomicallyAsync(path, writer => WriteRunRecord(writer, record), cancellationToken);
			PruneRunRecords(directory, DateTimeOffset.UtcNow);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async ValueTask<StoredPackageState> ReadStoredPackageStateWithoutLockAsync(
		string path,
		CancellationToken cancellationToken)
	{
		await using var stream = File.OpenRead(path);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		return ParseStoredPackageState(document.RootElement);
	}

	private static StoredPackageState ParseStoredPackageState(JsonElement root)
	{
		if (root.ValueKind is not JsonValueKind.Object)
			throw new InvalidDataException("Automation package state must be a JSON object.");

		string? trustedFingerprint = null;
		var tools = ImmutableDictionary.CreateBuilder<string, AutomationExternalToolConfiguration>(StringComparer.Ordinal);
		var actionSettings = ImmutableDictionary.CreateBuilder<AutomationActionLocalId, AutomationActionSettings>();
		foreach (var property in root.EnumerateObject())
		{
			switch (property.Name)
			{
				case "trustedFingerprint":
					trustedFingerprint = property.Value.ValueKind is JsonValueKind.Null ? null : property.Value.GetString();
					break;
				case "externalTools":
					foreach (var tool in property.Value.EnumerateObject())
						tools.Add(tool.Name, new(tool.Name, tool.Value.GetString() ?? string.Empty));
					break;
				case "actionSettings":
					foreach (var action in property.Value.EnumerateObject())
						actionSettings.Add(AutomationActionLocalId.Parse(action.Name), ParseActionSettings(action.Value));
					break;
				default:
					throw new InvalidDataException($"Unknown automation package state property '{property.Name}'.");
			}
		}

		return new(trustedFingerprint, tools.ToImmutable(), actionSettings.ToImmutable());
	}

	private static AutomationActionSettings ParseActionSettings(JsonElement root)
	{
		if (root.ValueKind is not JsonValueKind.Object)
			throw new InvalidDataException("Automation action settings must be a JSON object.");

		var settings = ImmutableDictionary.CreateBuilder<string, AutomationSettingValue>(StringComparer.Ordinal);
		foreach (var property in root.EnumerateObject())
		{
			if (property.Name is not "settings")
				throw new InvalidDataException($"Unknown automation action settings property '{property.Name}'.");
			foreach (var setting in property.Value.EnumerateObject())
				settings.Add(setting.Name, ParseSetting(setting.Value));
		}

		return new(settings.ToImmutable());
	}

	private static AutomationSettingValue ParseSetting(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.True => new(AutomationSettingValueKind.Boolean, BooleanValue: true),
		JsonValueKind.False => new(AutomationSettingValueKind.Boolean),
		JsonValueKind.Number when value.TryGetInt64(out var integer) => new(AutomationSettingValueKind.Integer, IntegerValue: integer),
		JsonValueKind.Number => new(AutomationSettingValueKind.Number, NumberValue: value.GetDouble()),
		JsonValueKind.String => new(AutomationSettingValueKind.String, StringValue: value.GetString()),
		_ => throw new InvalidDataException("Automation setting values must be boolean, number, integer, or string values."),
	};

	private static async ValueTask WriteJsonAtomicallyAsync(
		string path,
		Action<Utf8JsonWriter> write,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temporaryPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
		try
		{
			await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
			{
				using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
				write(writer);
				await writer.FlushAsync(cancellationToken);
				await stream.FlushAsync(cancellationToken);
			}

			File.Move(temporaryPath, path, true);
		}
		finally
		{
			File.Delete(temporaryPath);
		}
	}

	private static void WriteStoredPackageState(Utf8JsonWriter writer, StoredPackageState state)
	{
		writer.WriteStartObject();
		if (state.TrustedFingerprint is null)
			writer.WriteNull("trustedFingerprint");
		else
			writer.WriteString("trustedFingerprint", state.TrustedFingerprint);
		writer.WriteStartObject("externalTools");
		foreach (var tool in state.ExternalTools.OrderBy(item => item.Key, StringComparer.Ordinal))
			writer.WriteString(tool.Key, tool.Value.ExecutablePath);
		writer.WriteEndObject();
		writer.WriteStartObject("actionSettings");
		foreach (var (localId, settings) in state.ActionSettings.OrderBy(item => item.Key.Value, StringComparer.Ordinal))
		{
			writer.WritePropertyName(localId.Value);
			WriteActionSettings(writer, settings);
		}

		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	/// <summary>
	/// Writes settings in the shape <see cref="ParseActionSettings"/> reads, ordered so that saving the same
	/// configuration twice produces the same bytes.
	/// </summary>
	private static void WriteActionSettings(Utf8JsonWriter writer, AutomationActionSettings settings)
	{
		writer.WriteStartObject();
		writer.WriteStartObject("settings");
		foreach (var (key, value) in settings.Values.OrderBy(setting => setting.Key, StringComparer.Ordinal))
		{
			writer.WritePropertyName(key);
			AutomationSettingValueJson.Write(writer, value);
		}

		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	private static void WriteRunRecord(Utf8JsonWriter writer, AutomationRunRecord record)
	{
		var snapshot = record.Snapshot;
		writer.WriteStartObject();
		writer.WriteString("runId", snapshot.Id.Value);
		writer.WriteString("actionId", snapshot.ActionId.Value);
		writer.WriteString("packageId", snapshot.ActionId.PackageId.Value);
		writer.WriteString("packageVersion", record.PackageVersion);
		writer.WriteString("trustFingerprint", record.TrustFingerprint);
		writer.WriteString("state", snapshot.State.ToString());
		writer.WriteString("startedAtUtc", snapshot.StartedAtUtc);
		if (snapshot.CompletedAtUtc is { } completed)
			writer.WriteString("completedAtUtc", completed);
		writer.WriteString("activeFolderPath", snapshot.Selection.ActiveFolderPath);
		writer.WriteStartArray("selectedPaths");
		foreach (var item in snapshot.Selection.Items)
		{
			writer.WriteStartObject();
			writer.WriteString("path", item.FullPath);
			writer.WriteString("kind", item.Kind.ToString());
			writer.WriteString("locationKind", item.LocationKind.ToString());
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		if (snapshot.ProgressPercent is { } progress)
			writer.WriteNumber("progressPercent", progress);
		writer.WriteString("status", snapshot.Status);
		writer.WriteStartArray("logs");
		foreach (var log in snapshot.Logs)
		{
			writer.WriteStartObject();
			writer.WriteNumber("sequence", log.Sequence);
			writer.WriteString("level", log.Level.ToString());
			writer.WriteString("message", log.Message);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WriteString("standardError", snapshot.StandardError);
		if (snapshot.Failure is not null)
			writer.WriteString("failure", snapshot.Failure);
		writer.WriteBoolean("standardErrorTruncated", snapshot.StandardErrorTruncated);
		writer.WriteEndObject();
	}

	private void PruneRunRecords(string directory, DateTimeOffset now)
	{
		var files = new DirectoryInfo(directory).EnumerateFiles("*.json")
			.OrderByDescending(file => file.Name, StringComparer.Ordinal)
			.ToArray();
		long retainedBytes = 0;
		foreach (var file in files)
		{
			var expired = now - file.LastWriteTimeUtc > _runRecordLifetime;
			if (expired || retainedBytes + file.Length > _runRecordSizeBudget)
				file.Delete();
			else
				retainedBytes += file.Length;
		}
	}

	private string GetPackageStatePath(AutomationPackageId packageId)
		=> Path.Join(_stateRoot, "packages", RequireFileNameSafe(packageId.Value) + ".json");

	private static string RequireFileNameSafe(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			throw new ArgumentException($"Automation id '{value}' is not valid for persistent state.", nameof(value));
		return value;
	}

	private static AutomationPackageState EmptyPackageState()
		=> new(null, ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty);

	private static StoredPackageState EmptyStoredPackageState()
		=> new(
			null,
			ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty,
			ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings>.Empty);

	private static AutomationActionSettings EmptyActionSettings()
		=> new(ImmutableDictionary<string, AutomationSettingValue>.Empty);

	private sealed record StoredPackageState(
		string? TrustedFingerprint,
		ImmutableDictionary<string, AutomationExternalToolConfiguration> ExternalTools,
		ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings> ActionSettings);
}
