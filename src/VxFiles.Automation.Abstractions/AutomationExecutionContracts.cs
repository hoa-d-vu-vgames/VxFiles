// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.ComponentModel;

namespace VxFiles.Automation.Abstractions;

public readonly record struct AutomationRunId(Guid Value);

/// <summary>
/// The host's folder/selection revision at capture time. Result routing is rejected when the host has moved on.
/// </summary>
public readonly record struct HostRevision(long Value);

public enum SelectedPathKind
{
	File,
	Folder,
}

public enum SelectedLocationKind
{
	Local,
	Unc,
}

public enum AutomationRunState
{
	Starting,
	Running,
	Cancelling,
	Succeeded,
	Failed,
	TimedOut,
	Cancelled,
}

public enum AutomationLogLevel
{
	Debug,
	Information,
	Warning,
	Error,
}

public enum AutomationIntentDisposition
{
	Applied,
	Rejected,
	Stale,
}

public sealed record SelectedPath(
	string FullPath,
	SelectedPathKind Kind,
	SelectedLocationKind LocationKind);

/// <summary>
/// The immutable folder-and-selection input captured when an Automation Action is invoked.
/// </summary>
public sealed record SelectionSnapshot(
	string ActiveFolderPath,
	DateTimeOffset CapturedAtUtc,
	ImmutableArray<SelectedPath> Items);

public sealed record AutomationInvocation(
	AutomationActionId ActionId,
	long CatalogRevision,
	HostRevision HostRevision,
	SelectionSnapshot Selection);

public sealed record AutomationLogEntry(
	long Sequence,
	AutomationLogLevel Level,
	string Message);

public abstract record AutomationResultIntent
{
	private AutomationResultIntent()
	{
	}

	public sealed record RefreshCurrentFolder : AutomationResultIntent;

	public sealed record RevealPaths(ImmutableArray<string> Paths) : AutomationResultIntent;
}

public sealed record AutomationIntentResult(
	AutomationResultIntent Intent,
	AutomationIntentDisposition Disposition,
	string Message);

public sealed record AutomationRunSnapshot(
	AutomationRunId Id,
	AutomationActionId ActionId,
	AutomationRunState State,
	SelectionSnapshot Selection,
	HostRevision HostRevision,
	DateTimeOffset StartedAtUtc,
	DateTimeOffset? CompletedAtUtc,
	double? ProgressPercent,
	string Status,
	ImmutableArray<AutomationLogEntry> Logs,
	string StandardError,
	bool StandardErrorTruncated,
	ImmutableArray<AutomationIntentResult> IntentResults,
	string? Failure);

/// <summary>
/// Everything a host surface needs to render Automation Tools: the package/action hierarchy plus run activity.
/// </summary>
public sealed record AutomationSnapshot(
	long Revision,
	long CatalogRevision,
	ImmutableArray<AutomationPackageSnapshot> Packages,
	ImmutableArray<AutomationRunSnapshot> ActiveRuns,
	ImmutableArray<AutomationRunSnapshot> RecentRuns);

public enum AutomationSettingValueKind
{
	Boolean,
	Integer,
	Number,
	String,
}

public sealed record AutomationSettingValue(
	AutomationSettingValueKind Kind,
	bool BooleanValue = false,
	long IntegerValue = 0,
	double NumberValue = 0,
	string? StringValue = null);

public sealed record AutomationExternalToolConfiguration(
	string Id,
	string ExecutablePath);

/// <summary>
/// Trust and shared external-tool configuration belong to the whole Automation Package.
/// </summary>
public sealed record AutomationPackageState(
	string? TrustedFingerprint,
	ImmutableDictionary<string, AutomationExternalToolConfiguration> ExternalTools);

/// <summary>
/// Typed settings belong to a single Automation Action inside its package.
/// </summary>
public sealed record AutomationActionSettings(
	ImmutableDictionary<string, AutomationSettingValue> Values);

/// <summary>
/// Everything a user configured for one Automation Package, applied as a single unit.
/// </summary>
/// <remarks>
/// Package-scoped because that is what the user is shown: one dialog, one Save.
///
/// <para>
/// Both dictionaries are read per entry rather than wholesale, and neither need be complete. A tool or action
/// the submission does not mention keeps what it had, so a dialog showing one action's settings does not have to
/// reconstruct the rest of the package to save it. Clearing a tool therefore has exactly one spelling — submit it
/// with an empty path — rather than two that are easy to confuse.
/// </para>
/// </remarks>
public sealed record AutomationPackageConfiguration(
	ImmutableDictionary<string, AutomationExternalToolConfiguration> ExternalTools,
	ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings> ActionSettings);

/// <remarks>
/// Identity rests on <paramref name="Fingerprint"/> alone: it is the only one of these the trust fingerprint
/// mixes in. Do not add a signature status. A correct check reports <c>unsigned</c> for the FFmpeg builds people
/// actually install, so the field can only lie or say nothing, and saying nothing is smaller.
/// </remarks>
public sealed record AutomationExternalToolIdentity(
	string Id,
	string ExecutablePath,
	string Fingerprint,
	string? FileVersion);

/// <summary>
/// Package-wide consent shown before the first run and whenever package, runner, or tool identity changes.
/// </summary>
public sealed record AutomationTrustRequest(
	AutomationPackageId PackageId,
	string DisplayName,
	string PackageVersion,
	string PackagePath,
	ImmutableArray<AutomationActionId> Actions,
	AutomationActionId RequestedAction,
	int SelectedItemCount,
	string TrustFingerprint,
	string RunnerFingerprint,
	ImmutableArray<AutomationExternalToolIdentity> ExternalTools);

public sealed record AutomationResultRoutingRequest(
	AutomationRunId RunId,
	HostRevision HostRevision,
	string CapturedFolderPath,
	ImmutableArray<AutomationResultIntent> Intents);

public sealed record AutomationRunRecord(
	AutomationRunSnapshot Snapshot,
	string PackageVersion,
	string TrustFingerprint);

/// <remarks>
/// The write members are plumbing the session writes through, not the seam a host configures a package by —
/// that is <see cref="IAutomationSession.ApplyPackageConfigurationAsync"/>. A store can only persist; it cannot
/// see the manifest a configuration has to be validated against, nor the snapshot the change has to reach.
/// </remarks>
public interface IAutomationStateStore
{
	ValueTask<AutomationPackageState> ReadPackageStateAsync(
		AutomationPackageId packageId,
		CancellationToken cancellationToken = default);

	ValueTask WritePackageTrustAsync(
		AutomationPackageId packageId,
		string fingerprint,
		CancellationToken cancellationToken = default);

	ValueTask<AutomationActionSettings> ReadActionSettingsAsync(
		AutomationActionId actionId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically replaces a package's external tools and the submitted actions' settings, leaving trust and
	/// omitted actions exactly as they were.
	/// </summary>
	/// <remarks>
	/// Trust is neither renewed nor revoked here. The fingerprint mixes in each resolved tool's SHA-256, so
	/// selecting different content invalidates trust without turning one dialog Save into several durable writes.
	/// </remarks>
	ValueTask WritePackageConfigurationAsync(
		AutomationPackageId packageId,
		ImmutableDictionary<string, AutomationExternalToolConfiguration> externalTools,
		ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings> actionSettings,
		CancellationToken cancellationToken = default);

	ValueTask AppendRunRecordAsync(
		AutomationRunRecord record,
		CancellationToken cancellationToken = default);
}

public interface IAutomationTrustConsent
{
	ValueTask<bool> RequestTrustAsync(
		AutomationTrustRequest request,
		CancellationToken cancellationToken = default);
}

public interface IAutomationResultRouter
{
	ValueTask<ImmutableArray<AutomationIntentResult>> RouteAsync(
		AutomationResultRoutingRequest request,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// The whole external surface of the headless Automation module.
/// </summary>
public interface IAutomationSession : INotifyPropertyChanged, IAsyncDisposable
{
	AutomationSnapshot Snapshot { get; }

	ValueTask InvokeAsync(
		AutomationInvocation invocation,
		CancellationToken cancellationToken = default);

	ValueTask CancelAsync(
		AutomationRunId runId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Validates a whole package's configuration, persists it, and republishes the package it changes.
	/// </summary>
	/// <remarks>
	/// One member rather than a store handed to the host, because applying a configuration owes three things and
	/// a store can only do the middle one: what is valid is declared by the manifest, and what is published is
	/// this session's snapshot.
	///
	/// <para>
	/// Nothing is written until everything validates, and the accepted package configuration is persisted as one
	/// atomic replacement. A refused or failed Save therefore leaves the entire previous configuration intact.
	/// </para>
	///
	/// <para>
	/// Throws <see cref="InvalidOperationException"/> for a package or action the catalog does not have, a tool or
	/// setting the manifest does not declare, a value outside what a setting accepts, and a tool path that is not
	/// usable. A path known to be bad is refused rather than stored: an unconfigured tool is a state the run path
	/// knows how to ask about, whereas a stored bad path would read as configured-and-broken for as long as it sat
	/// there. To clear a tool, submit it with an empty path.
	/// </para>
	/// </remarks>
	ValueTask ApplyPackageConfigurationAsync(
		AutomationPackageId packageId,
		AutomationPackageConfiguration configuration,
		CancellationToken cancellationToken = default);
}
