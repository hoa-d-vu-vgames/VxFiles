// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// What a host is told about an Automation Package whose external tools the user has not pointed at yet, or has
/// pointed at something that is no longer there.
/// </summary>
[TestClass]
public sealed class AutomationReadinessTests
{
	private const string PackageTools = """

		  "externalTools": [
		    { "id": "hoa.ffmpeg", "displayName": "FFmpeg" },
		    { "id": "hoa.ffprobe", "displayName": "FFprobe" }
		  ],
		""";

	/// <summary>
	/// The state a bundled package is in on a clean install. Reporting it as available and failing after the click
	/// is how a package that is working correctly greets its first user as broken.
	/// </summary>
	[TestMethod]
	public async Task An_unconfigured_tool_needs_configuration_rather_than_reporting_available()
	{
		using var fixture = AutomationFixture.Create();
		AddMediaPackage(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var package = PackageOf(session);
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, package.Availability);
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, ActionOf(session, "convert").Availability);

		// Not a fault, and not the verdict a failed run leaves behind.
		Assert.AreNotEqual(AutomationAvailability.MissingDependency, package.Availability);
	}

	/// <summary>
	/// Readiness is per action for this reason: three of Media Tools' four actions never touch FFprobe, and a
	/// package-wide verdict would stop them all on behalf of the one that does.
	/// </summary>
	[TestMethod]
	public async Task Configuring_one_tool_leaves_only_the_action_needing_the_other_unready()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddMediaPackage(fixture);
		var store = new MemoryStateStore();
		Configure(store, ("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe")));
		await using var session = await OpenAsync(fixture, store);

		foreach (var localId in new[] { "convert", "thumbnails", "compress" })
			Assert.AreEqual(AutomationAvailability.Available, ActionOf(session, localId).Availability, localId);

		var inspect = ActionOf(session, "inspect");
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, inspect.Availability);

		// Named, because which of the two tools is missing is the whole of what the user has to act on.
		StringAssert.Contains(string.Join(" ", inspect.Diagnostics), "FFprobe");

		// One action short is still the package's problem, so the Configure route stays reachable from its row.
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, PackageOf(session).Availability);
	}

	/// <summary>
	/// Configured is not the same as installed. An uninstall leaves the stored path behind it, and the tool it
	/// named is gone.
	/// </summary>
	[TestMethod]
	public async Task A_tool_whose_executable_is_gone_needs_configuring_again()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddMediaPackage(fixture);
		var store = new MemoryStateStore();
		var executable = tools.AddExecutable("ffmpeg.exe");
		Configure(store, ("hoa.ffmpeg", executable), ("hoa.ffprobe", tools.AddExecutable("ffprobe.exe")));
		File.Delete(executable);

		await using var session = await OpenAsync(fixture, store);

		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, ActionOf(session, "convert").Availability);
		Assert.AreEqual(AutomationAvailability.Available, ActionOf(session, "inspect").Availability);
	}

	/// <summary>
	/// The reason readiness is composed on every projection rather than recorded once: a refresh rebuilds the
	/// packages from manifests, which know nothing about what the user configured, so anything merely stored on
	/// the snapshot is erased by the next thing that touches a package folder.
	/// </summary>
	[TestMethod]
	public async Task Readiness_survives_a_catalog_refresh()
	{
		using var fixture = AutomationFixture.Create();
		AddMediaPackage(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, PackageOf(session).Availability);

		fixture.UpdateFile("media", "convert.py", "pass  # touched");
		await AutomationFixture.WaitForCatalogRevisionAsync(session, session.Snapshot.CatalogRevision + 1);

		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, PackageOf(session).Availability);
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, ActionOf(session, "convert").Availability);
	}

	[TestMethod]
	public async Task Configuring_the_last_tool_makes_its_actions_available_without_a_restart()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddMediaPackage(fixture);
		var store = new MemoryStateStore();
		Configure(store, ("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe")));
		await using var session = await OpenAsync(fixture, store);
		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, ActionOf(session, "inspect").Availability);

		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			new(
				ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty.Add(
					"hoa.ffprobe",
					new("hoa.ffprobe", tools.AddExecutable("ffprobe.exe"))),
				ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings>.Empty));

		Assert.AreEqual(AutomationAvailability.Available, ActionOf(session, "inspect").Availability);
		Assert.AreEqual(AutomationAvailability.Available, PackageOf(session).Availability);
		Assert.IsEmpty(ActionOf(session, "inspect").Diagnostics);
	}

	/// <summary>
	/// An action whose manifest is broken cannot be configured into working, so it keeps the verdict that says so
	/// rather than being handed one that points the user at a dialog which would not help.
	/// </summary>
	[TestMethod]
	public async Task An_action_that_failed_validation_is_not_reported_as_needing_configuration()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.PackageWith(
				AutomationManifests.DefaultPackageId,
				PackageTools,
				AutomationManifests.Action("convert", "convert.py", extraProperties: Needs("hoa.ffmpeg")),
				AutomationManifests.Action("broken", "absent.py", extraProperties: Needs("hoa.ffmpeg"))),
			("convert.py", "pass"));

		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		Assert.AreEqual(AutomationAvailability.NeedsConfiguration, ActionOf(session, "convert").Availability);
		Assert.AreEqual(AutomationAvailability.Disabled, ActionOf(session, "broken").Availability);
	}

	/// <summary>
	/// Readiness is asked on every projection, so a package that declares no external tool and no setting has to
	/// cost nothing at all — otherwise the bundled tracer would pay for a feature it does not use on every
	/// keystroke that touches a package folder.
	/// </summary>
	[TestMethod]
	public async Task A_package_declaring_no_external_tool_is_never_read_for()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"tracer",
			AutomationManifests.Package("hoa.tracer", AutomationManifests.Action("trace", "trace.py")),
			("trace.py", "pass"));
		var store = new CountingStateStore();

		await using var session = await OpenAsync(fixture, store);

		Assert.AreEqual(AutomationAvailability.Available, PackageOf(session).Availability);
		Assert.AreEqual(0, store.PackageStateReads);
	}

	private static void AddMediaPackage(AutomationFixture fixture)
		=> fixture.AddPackage(
			"media",
			AutomationManifests.PackageWith(
				AutomationManifests.DefaultPackageId,
				PackageTools,
				AutomationManifests.Action("convert", "convert.py", extraProperties: Needs("hoa.ffmpeg")),
				AutomationManifests.Action("thumbnails", "thumbnails.py", extraProperties: Needs("hoa.ffmpeg")),
				AutomationManifests.Action("compress", "compress.py", extraProperties: Needs("hoa.ffmpeg")),
				AutomationManifests.Action("inspect", "inspect.py", extraProperties: Needs("hoa.ffprobe"))),
			("convert.py", "pass"),
			("thumbnails.py", "pass"),
			("compress.py", "pass"),
			("inspect.py", "pass"));

	private static string Needs(string toolId)
		=> $"{Environment.NewLine}  \"externalTools\": [\"{toolId}\"],";

	private static void Configure(MemoryStateStore store, params (string Id, string Path)[] tools)
		=> store.ConfigureExternalTools(
			AutomationManifests.DefaultPackageId,
			tools.ToImmutableDictionary(tool => tool.Id, tool => new AutomationExternalToolConfiguration(tool.Id, tool.Path)));

	private static ValueTask<IAutomationSession> OpenAsync(AutomationFixture fixture, IAutomationStateStore store)
		=> AutomationModule.OpenAsync(fixture.Options, store, new AcceptingTrustConsent(), new RecordingResultRouter());

	private static AutomationPackageSnapshot PackageOf(IAutomationSession session)
		=> session.Snapshot.Packages.Single();

	private static AutomationActionSnapshot ActionOf(IAutomationSession session, string localId)
		=> PackageOf(session).Actions.Single(action => action.Id.LocalId.Value == localId);

	/// <summary>
	/// Counts what a projection asks the store for, so the claim that an unconfigurable package costs nothing is
	/// asserted rather than assumed.
	/// </summary>
	private sealed class CountingStateStore : IAutomationStateStore
	{
		public int PackageStateReads { get; private set; }

		public ValueTask<AutomationPackageState> ReadPackageStateAsync(
			AutomationPackageId packageId,
			CancellationToken cancellationToken = default)
		{
			PackageStateReads++;
			return ValueTask.FromResult(
				new AutomationPackageState(null, ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty));
		}

		public ValueTask WritePackageTrustAsync(
			AutomationPackageId packageId,
			string fingerprint,
			CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;

		public ValueTask WriteExternalToolsAsync(
			AutomationPackageId packageId,
			ImmutableDictionary<string, AutomationExternalToolConfiguration> externalTools,
			CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;

		public ValueTask<AutomationActionSettings> ReadActionSettingsAsync(
			AutomationActionId actionId,
			CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(new AutomationActionSettings(ImmutableDictionary<string, AutomationSettingValue>.Empty));

		public ValueTask WriteActionSettingsAsync(
			AutomationActionId actionId,
			AutomationActionSettings settings,
			CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;

		public ValueTask AppendRunRecordAsync(AutomationRunRecord record, CancellationToken cancellationToken = default)
			=> ValueTask.CompletedTask;
	}

	private sealed class ToolFolder : IDisposable
	{
		public string Path { get; } = System.IO.Path.Join(
			System.IO.Path.GetTempPath(),
			$"VxFiles-Automation-Readiness-{Guid.NewGuid():N}");

		public ToolFolder()
			=> Directory.CreateDirectory(Path);

		public string AddExecutable(string name)
		{
			var path = System.IO.Path.Join(Path, name);
			File.WriteAllBytes(path, Encoding.UTF8.GetBytes(name));
			return path;
		}

		public void Dispose()
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, true);
		}
	}
}
