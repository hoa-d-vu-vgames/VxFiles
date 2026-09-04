// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// Configuring an Automation Package: what a Save accepts, what it refuses, and what a host sees afterwards
/// without restarting.
/// </summary>
[TestClass]
public sealed class AutomationPackageConfigurationTests
{
	private const string PackageTools = """

		  "externalTools": [
		    { "id": "hoa.ffmpeg", "displayName": "FFmpeg" },
		    { "id": "hoa.exiftool", "displayName": "ExifTool" }
		  ],
		""";

	private const string ActionSettings = """

		  "settings": [
		    {
		      "key": "quality",
		      "displayName": "Quality",
		      "description": "Constant rate factor.",
		      "type": "integer",
		      "default": 20,
		      "minimum": 0,
		      "maximum": 51
		    }
		  ],
		  "externalTools": ["hoa.ffmpeg"],
		""";

	[TestMethod]
	public async Task Applying_a_configuration_persists_the_tools_and_the_settings()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var store = new MemoryStateStore();
		await using var session = await OpenAsync(fixture, store);

		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			Configuration(
				tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe")), ("hoa.exiftool", tools.AddExecutable("exiftool.exe"))],
				settings: [("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 31))]));

		var stored = store.ExternalToolsFor(AutomationManifests.DefaultPackageId);
		Assert.AreEqual(2, stored.Count);
		StringAssert.EndsWith(stored["hoa.ffmpeg"].ExecutablePath, "ffmpeg.exe");
		Assert.AreEqual(
			31L,
			(await store.ReadActionSettingsAsync(AutomationFixture.ParseActionId("hoa.media/convert"))).Values["quality"].IntegerValue);

		// Both halves of one Save: persisted, and reflected in what the host is already bound to.
		Assert.AreEqual(31L, SettingOf(session).CurrentValue.IntegerValue);
		Assert.AreEqual(AutomationAvailability.Available, PackageOf(session).Availability);
	}

	/// <summary>
	/// A dialog that saves one action's settings must not unconfigure the package's tools by not mentioning them.
	/// Clearing a tool has one spelling, and this is not it.
	/// </summary>
	[TestMethod]
	public async Task A_tool_the_submission_does_not_mention_keeps_what_it_had()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var store = new MemoryStateStore();
		await using var session = await OpenAsync(fixture, store);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);
		await session.ApplyPackageConfigurationAsync(
			packageId,
			Configuration(tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe"))]));

		await session.ApplyPackageConfigurationAsync(
			packageId,
			Configuration(settings: [("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 31))]));

		Assert.HasCount(1, store.ExternalToolsFor(AutomationManifests.DefaultPackageId));
	}

	/// <summary>
	/// The point of applying a whole package at once: the host does not reopen the session to see the result.
	/// </summary>
	[TestMethod]
	public async Task An_applied_setting_reaches_the_published_snapshot_without_a_restart()
	{
		using var fixture = AutomationFixture.Create();
		AddPackage(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());
		Assert.AreEqual(20L, SettingOf(session).CurrentValue.IntegerValue);
		var catalogRevision = session.Snapshot.CatalogRevision;

		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			Configuration(settings: [("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 31))]));

		Assert.AreEqual(31L, SettingOf(session).CurrentValue.IntegerValue);
		Assert.AreEqual(20L, SettingOf(session).DefaultValue.IntegerValue);
		Assert.AreEqual(catalogRevision + 1, session.Snapshot.CatalogRevision);
	}

	/// <summary>
	/// A run that could not resolve a tool marks its package, and that mark is what the user acted on. Applying
	/// the configuration has to take it back, or the package stays accusing after it was fixed.
	/// </summary>
	[TestMethod]
	public async Task Configuring_a_missing_tool_clears_the_mark_the_failed_run_left()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		await Assert.ThrowsExactlyAsync<AutomationMissingDependencyException>(
			() => session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert")).AsTask());
		Assert.AreEqual(AutomationAvailability.MissingDependency, PackageOf(session).Availability);

		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			Configuration(tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe"))]));

		Assert.AreEqual(AutomationAvailability.Available, PackageOf(session).Availability);
		Assert.IsEmpty(PackageOf(session).Diagnostics);
	}

	[TestMethod]
	public async Task An_invalid_path_is_refused_and_the_valid_entries_beside_it_are_not_written()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var store = new MemoryStateStore();
		await using var session = await OpenAsync(fixture, store);

		await Assert.ThrowsExactlyAsync<AutomationMissingDependencyException>(
			() => session.ApplyPackageConfigurationAsync(
				AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
				Configuration(
					tools:
					[
						("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe")),
						("hoa.exiftool", Path.Join(tools.Path, "absent.exe")),
					],
					settings: [("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 31))])).AsTask());

		Assert.IsEmpty(store.ExternalToolsFor(AutomationManifests.DefaultPackageId));
		Assert.IsEmpty((await store.ReadActionSettingsAsync(AutomationFixture.ParseActionId("hoa.media/convert"))).Values);
		Assert.AreEqual(20L, SettingOf(session).CurrentValue.IntegerValue);
	}

	[TestMethod]
	public async Task An_empty_path_clears_a_configured_tool()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var store = new MemoryStateStore();
		await using var session = await OpenAsync(fixture, store);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);
		await session.ApplyPackageConfigurationAsync(
			packageId,
			Configuration(tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe"))]));

		await session.ApplyPackageConfigurationAsync(packageId, Configuration(tools: [("hoa.ffmpeg", string.Empty)]));

		Assert.IsEmpty(store.ExternalToolsFor(AutomationManifests.DefaultPackageId));
	}

	[TestMethod]
	public async Task A_value_outside_what_a_setting_accepts_is_refused()
	{
		using var fixture = AutomationFixture.Create();
		AddPackage(fixture);
		var store = new MemoryStateStore();
		await using var session = await OpenAsync(fixture, store);

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.ApplyPackageConfigurationAsync(
				AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
				Configuration(settings: [("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 99))])).AsTask());

		// Named by what the user was shown, not by the manifest key they never saw.
		StringAssert.Contains(exception.Message, "Quality");
		Assert.IsEmpty((await store.ReadActionSettingsAsync(AutomationFixture.ParseActionId("hoa.media/convert"))).Values);
	}

	[TestMethod]
	public async Task A_tool_the_package_does_not_declare_is_refused()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.ApplyPackageConfigurationAsync(
				AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
				Configuration(tools: [("hoa.imagemagick", tools.AddExecutable("magick.exe"))])).AsTask());
	}

	/// <summary>
	/// Applying a configuration never clears trust, so re-saving an unchanged one is silent.
	/// </summary>
	[TestMethod]
	public async Task Re_applying_the_same_path_does_not_ask_for_trust_again()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var trust = new AcceptingTrustConsent();
		await using var session = await OpenAsync(fixture, new MemoryStateStore(), trust);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);
		var executable = tools.AddExecutable("ffmpeg.exe");
		await session.ApplyPackageConfigurationAsync(packageId, Configuration(tools: [("hoa.ffmpeg", executable)]));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		Assert.AreEqual(1, trust.RequestCount);

		await session.ApplyPackageConfigurationAsync(packageId, Configuration(tools: [("hoa.ffmpeg", executable)]));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));

		Assert.AreEqual(1, trust.RequestCount, "Re-applying an unchanged configuration asked for trust again.");
	}

	/// <summary>
	/// The case the spelling rule is actually for: the same executable reached through a shim. Trust rests on the
	/// target's SHA-256, so pointing at the shim instead is not a new tool — which is what a winget or scoop
	/// upgrade produces every time it re-points its shim.
	/// </summary>
	[TestMethod]
	public async Task Re_applying_the_same_executable_through_a_shim_does_not_ask_for_trust_again()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var trust = new AcceptingTrustConsent();
		await using var session = await OpenAsync(fixture, new MemoryStateStore(), trust);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);
		var target = tools.AddExecutable("ffmpeg.exe");
		await session.ApplyPackageConfigurationAsync(packageId, Configuration(tools: [("hoa.ffmpeg", target)]));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		Assert.AreEqual(1, trust.RequestCount);

		var shim = TestLinks.Create(Path.Join(tools.Path, "shim.exe"), target);
		await session.ApplyPackageConfigurationAsync(packageId, Configuration(tools: [("hoa.ffmpeg", shim)]));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));

		Assert.AreEqual(1, trust.RequestCount, "The same executable reached through a shim asked for trust again.");
	}

	[TestMethod]
	public async Task Applying_a_different_executable_asks_for_trust_again()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var trust = new AcceptingTrustConsent();
		await using var session = await OpenAsync(fixture, new MemoryStateStore(), trust);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);
		await session.ApplyPackageConfigurationAsync(
			packageId,
			Configuration(tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe"))]));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		Assert.AreEqual(1, trust.RequestCount);

		await session.ApplyPackageConfigurationAsync(
			packageId,
			Configuration(tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg-7.exe"))]));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));

		Assert.AreEqual(2, trust.RequestCount);
	}

	[TestMethod]
	public async Task Package_trust_covers_every_configured_tool_used_by_its_actions()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		fixture.AddPackage(
			"media",
			AutomationManifests.PackageWith(
				AutomationManifests.DefaultPackageId,
				PackageTools,
				AutomationManifests.Action("convert", "convert.py", extraProperties: ToolReference("hoa.ffmpeg")),
				AutomationManifests.Action("inspect", "inspect.py", extraProperties: ToolReference("hoa.exiftool"))),
			("convert.py", "pass"),
			("inspect.py", "pass"));
		var store = new MemoryStateStore();
		store.ConfigureExternalTools(
			AutomationManifests.DefaultPackageId,
			new[]
			{
				("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe")),
				("hoa.exiftool", tools.AddExecutable("exiftool.exe")),
			}.ToImmutableDictionary(
				tool => tool.Item1,
				tool => new AutomationExternalToolConfiguration(tool.Item1, tool.Item2),
				StringComparer.Ordinal));
		var trust = new AcceptingTrustConsent();
		await using var session = await OpenAsync(fixture, store, trust);

		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert"));
		await session.InvokeAsync(fixture.Invocation(session, "hoa.media/inspect"));

		Assert.AreEqual(1, trust.RequestCount, "Sibling actions alternated package trust prompts.");
		CollectionAssert.AreEquivalent(
			new[] { "hoa.ffmpeg", "hoa.exiftool" },
			trust.Requests[0].ExternalTools.Select(tool => tool.Id).ToArray());
	}

	[TestMethod]
	public async Task A_tool_changed_during_consent_is_refused_before_the_action_starts()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		fixture.AddPackage(
			"media",
			AutomationManifests.PackageWith(
				AutomationManifests.DefaultPackageId,
				PackageTools,
				AutomationManifests.Action("convert", "convert.py", extraProperties: ToolReference("hoa.ffmpeg"))),
			("convert.py", "pass"));
		var executable = tools.AddExecutable("ffmpeg.exe");
		var store = new MemoryStateStore();
		store.ConfigureExternalTools(
			AutomationManifests.DefaultPackageId,
			ImmutableDictionary<string, AutomationExternalToolConfiguration>.Empty.Add(
				"hoa.ffmpeg",
				new("hoa.ffmpeg", executable)));
		var trust = new CallbackTrustConsent(() => File.AppendAllText(executable, "changed"));
		await using var session = await OpenAsync(fixture, store, trust);

		var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => session.InvokeAsync(fixture.Invocation(session, "hoa.media/convert")).AsTask());

		StringAssert.Contains(exception.Message, "changed after trust approval");
		Assert.IsEmpty(session.Snapshot.RecentRuns);
	}

	/// <summary>
	/// JSON has one number type, so a whole number written for a <c>number</c> setting is read back as an
	/// integer. The declaration decides what it is, or a number setting could never hold 2.
	/// </summary>
	[TestMethod]
	public async Task A_whole_number_survives_the_round_trip_through_the_file_store()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py", extraProperties: """

					  "settings": [
					    {
					      "key": "speed",
					      "displayName": "Speed",
					      "description": "Playback rate.",
					      "type": "number",
					      "default": 1.5
					    }
					  ],
					""")),
			("convert.py", "pass"));
		var store = new FileAutomationStateStore(fixture.Options.StateRoot);
		await using var session = await OpenAsync(fixture, store);

		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			Configuration(settings: [("speed", new(AutomationSettingValueKind.Number, NumberValue: 2))]));

		var setting = SettingOf(session);
		Assert.AreEqual(AutomationSettingValueKind.Number, setting.CurrentValue.Kind);
		Assert.AreEqual(2d, setting.CurrentValue.NumberValue);
	}

	[TestMethod]
	public async Task The_file_store_commits_one_package_configuration_as_one_file()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var store = new FileAutomationStateStore(fixture.Options.StateRoot);
		await using var session = await OpenAsync(fixture, store);

		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			Configuration(
				tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe"))],
				settings: [("quality", new(AutomationSettingValueKind.Integer, IntegerValue: 31))]));

		var files = Directory.EnumerateFiles(fixture.Options.StateRoot, "*.json", SearchOption.AllDirectories).ToArray();
		Assert.HasCount(1, files, "One dialog Save must not be split across package and action files.");
		StringAssert.EndsWith(files[0], Path.Join("packages", $"{AutomationManifests.DefaultPackageId}.json"));
		Assert.AreEqual(
			31L,
			(await store.ReadActionSettingsAsync(AutomationFixture.ParseActionId("hoa.media/convert"))).Values["quality"].IntegerValue);
	}

	/// <summary>
	/// A path the platform cannot make sense of at all is a different mistake to an incomplete one, and neither
	/// may reach the caller as an exception: the dialog asks this question on every keystroke.
	/// </summary>
	[TestMethod]
	public void A_path_the_platform_refuses_is_reported_apart_from_an_incomplete_one()
	{
		Assert.AreEqual(
			AutomationToolPathVerdict.Malformed,
			AutomationExternalToolPathRules.Evaluate("C:\\tools\\ff\0mpeg.exe").Verdict);
		Assert.AreEqual(
			AutomationToolPathVerdict.NotAbsolute,
			AutomationExternalToolPathRules.Evaluate("ffmpeg.exe").Verdict);

		// A character Windows forbids in a name is not one the path APIs refuse; it simply is not there.
		Assert.AreEqual(
			AutomationToolPathVerdict.NotFound,
			AutomationExternalToolPathRules.Evaluate(@"C:\tools\ff|mpeg.exe").Verdict);
	}

	/// <summary>
	/// The store a shipped app uses, not the in-memory double: writing a tool must carry the package's trust
	/// across, or every configuration change would re-prompt.
	/// </summary>
	[TestMethod]
	public async Task The_file_store_carries_trust_across_a_tool_write()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		var store = new FileAutomationStateStore(fixture.Options.StateRoot);
		await using var session = await OpenAsync(fixture, store);
		var packageId = AutomationPackageId.Parse(AutomationManifests.DefaultPackageId);
		await store.WritePackageTrustAsync(packageId, "sha256:earlier");

		await session.ApplyPackageConfigurationAsync(
			packageId,
			Configuration(tools: [("hoa.ffmpeg", tools.AddExecutable("ffmpeg.exe"))]));

		var state = await store.ReadPackageStateAsync(packageId);
		Assert.AreEqual("sha256:earlier", state.TrustedFingerprint);
		Assert.HasCount(1, state.ExternalTools);
	}

	/// <summary>
	/// What a configuration surface binds to. It has to be able to open, and be cancelled, without reading
	/// anything — so the tools a package declares and the paths configured for them travel on the snapshot, the
	/// way each setting's current value already does.
	/// </summary>
	[TestMethod]
	public async Task The_snapshot_carries_the_declared_tools_and_what_is_configured_for_them()
	{
		using var fixture = AutomationFixture.Create();
		using var tools = new ToolFolder();
		AddPackage(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var declared = PackageOf(session).ExternalTools;
		Assert.HasCount(2, declared);
		Assert.AreEqual("FFmpeg", declared.Single(tool => tool.Id == "hoa.ffmpeg").DisplayName);
		Assert.IsTrue(declared.All(tool => tool.ConfiguredPath.Length is 0));

		var executable = tools.AddExecutable("ffmpeg.exe");
		await session.ApplyPackageConfigurationAsync(
			AutomationPackageId.Parse(AutomationManifests.DefaultPackageId),
			Configuration(tools: [("hoa.ffmpeg", executable)]));

		// As the user spelled it, not what it resolves to: an editor has to show them back what they typed.
		var configured = PackageOf(session).ExternalTools;
		Assert.AreEqual(executable, configured.Single(tool => tool.Id == "hoa.ffmpeg").ConfiguredPath);
		Assert.IsEmpty(configured.Single(tool => tool.Id == "hoa.exiftool").ConfiguredPath);
	}

	private static void AddPackage(AutomationFixture fixture)
		=> fixture.AddPackage(
			"media",
			AutomationManifests.PackageWith(
				AutomationManifests.DefaultPackageId,
				PackageTools,
				AutomationManifests.Action("convert", "convert.py", extraProperties: ActionSettings)),
			("convert.py", "pass"));

	private static ValueTask<IAutomationSession> OpenAsync(
		AutomationFixture fixture,
		IAutomationStateStore store,
		IAutomationTrustConsent? trust = null)
		=> AutomationModule.OpenAsync(fixture.Options, store, trust ?? new AcceptingTrustConsent(), new RecordingResultRouter());

	private static AutomationPackageConfiguration Configuration(
		(string Id, string Path)[]? tools = null,
		(string Key, AutomationSettingValue Value)[]? settings = null)
		=> new(
			(tools ?? []).ToImmutableDictionary(tool => tool.Id, tool => new AutomationExternalToolConfiguration(tool.Id, tool.Path)),
			settings is null
				? ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings>.Empty
				: ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings>.Empty.Add(
					AutomationActionLocalId.Parse("convert"),
					new(settings.ToImmutableDictionary(setting => setting.Key, setting => setting.Value))));

	private static string ToolReference(string toolId)
		=> $"{Environment.NewLine}  \"externalTools\": [\"{toolId}\"],";

	private static AutomationPackageSnapshot PackageOf(IAutomationSession session)
		=> session.Snapshot.Packages.Single();

	private static AutomationSettingSchema SettingOf(IAutomationSession session)
		=> PackageOf(session).Actions.Single().Settings.Single();

	private sealed class ToolFolder : IDisposable
	{
		public string Path { get; } = System.IO.Path.Join(
			System.IO.Path.GetTempPath(),
			$"VxFiles-Automation-Config-{Guid.NewGuid():N}");

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

	private sealed class CallbackTrustConsent(Action accepted) : IAutomationTrustConsent
	{
		public ValueTask<bool> RequestTrustAsync(
			AutomationTrustRequest request,
			CancellationToken cancellationToken = default)
		{
			accepted();
			return ValueTask.FromResult(true);
		}
	}
}
