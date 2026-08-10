// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation.Tests;

/// <summary>
/// What an action declares as settings, and what it is currently set to, as a host surface sees it. The two
/// come from different places — the manifest and the state store — and meet in the snapshot.
/// </summary>
[TestClass]
public sealed class AutomationSettingSchemaTests
{
	private const string EnumSetting = """

		  "settings": [
		    {
		      "key": "preset",
		      "displayName": "Encoding preset",
		      "description": "How hard the encoder works for a smaller file.",
		      "type": "enum",
		      "default": "balanced",
		      "values": ["fast", "balanced", "best"]
		    }
		  ],
		""";

	[TestMethod]
	public async Task Declared_setting_reaches_the_snapshot_with_its_display_text_and_permitted_values()
	{
		using var fixture = AutomationFixture.Create();
		AddPackageWithSetting(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var setting = SettingOf(session);

		Assert.AreEqual("preset", setting.Key);
		Assert.AreEqual("Encoding preset", setting.DisplayName);
		Assert.AreEqual("How hard the encoder works for a smaller file.", setting.Description);
		Assert.AreEqual(AutomationSettingType.Enum, setting.Type);
		Assert.AreEqual("balanced", setting.DefaultValue.StringValue);
		CollectionAssert.AreEqual(new[] { "fast", "balanced", "best" }, setting.Values.ToArray());
	}

	/// <summary>
	/// The declared type is not the storage kind: an enum, a file path and a folder path all store as a string,
	/// and a host that only had the kind could not tell a dropdown from a picker.
	/// </summary>
	[TestMethod]
	public async Task Declared_type_is_reported_separately_from_the_kind_the_value_is_stored_as()
	{
		using var fixture = AutomationFixture.Create();
		AddPackageWithSetting(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var setting = SettingOf(session);

		Assert.AreEqual(AutomationSettingType.Enum, setting.Type);
		Assert.AreEqual(AutomationSettingValueKind.String, setting.DefaultValue.Kind);
	}

	/// <summary>
	/// The bounds an editor has to enforce are per type, and mostly absent: an enum carries permitted values and
	/// no numeric range, a number carries a range and no values.
	/// </summary>
	[TestMethod]
	public async Task Numeric_bounds_reach_the_snapshot_and_length_bounds_stay_absent()
	{
		using var fixture = AutomationFixture.Create();
		fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py", extraProperties: """

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
					""")),
			("convert.py", "pass"));
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var setting = SettingOf(session);

		Assert.AreEqual(AutomationSettingType.Integer, setting.Type);
		Assert.AreEqual(0d, setting.Minimum);
		Assert.AreEqual(51d, setting.Maximum);
		Assert.IsNull(setting.MinimumLength);
		Assert.IsNull(setting.MaximumLength);
		Assert.IsEmpty(setting.Values);
		Assert.AreEqual(20L, setting.DefaultValue.IntegerValue);
	}

	[TestMethod]
	public async Task Current_value_is_the_default_when_nothing_is_stored()
	{
		using var fixture = AutomationFixture.Create();
		AddPackageWithSetting(fixture);
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var setting = SettingOf(session);

		Assert.AreEqual(setting.DefaultValue, setting.CurrentValue);
	}

	[TestMethod]
	public async Task Current_value_is_the_stored_one_when_the_action_has_been_configured()
	{
		using var fixture = AutomationFixture.Create();
		AddPackageWithSetting(fixture);
		var store = new MemoryStateStore();
		Configure(store, "best");
		await using var session = await OpenAsync(fixture, store);

		var setting = SettingOf(session);

		Assert.AreEqual("best", setting.CurrentValue.StringValue);
		Assert.AreEqual("balanced", setting.DefaultValue.StringValue);
	}

	/// <summary>
	/// A catalog change rebuilds every snapshot from the manifests, which know nothing about stored values. The
	/// stored value has to be applied again, or editing a package would silently reset what a host displays.
	/// </summary>
	[TestMethod]
	public async Task Current_value_survives_a_catalog_refresh()
	{
		using var fixture = AutomationFixture.Create();
		AddPackageWithSetting(fixture);
		var store = new MemoryStateStore();
		Configure(store, "best");
		await using var session = await OpenAsync(fixture, store);
		var revision = session.Snapshot.CatalogRevision;

		fixture.UpdateFile("media", "convert.py", "print('changed')");
		await AutomationFixture.WaitForCatalogRevisionAsync(session, revision + 1);

		Assert.AreEqual("best", SettingOf(session).CurrentValue.StringValue);
	}

	[TestMethod]
	public async Task An_action_that_declares_no_settings_carries_none()
	{
		using var fixture = AutomationFixture.CreateForBundledPackages();
		await using var session = await OpenAsync(fixture, new MemoryStateStore());

		var tracer = session.Snapshot.Packages.Single(package => package.Id.Value is "vxfiles.tracer");

		Assert.IsNotEmpty(tracer.Actions);
		foreach (var action in tracer.Actions)
			Assert.IsEmpty(action.Settings);
	}

	private static void AddPackageWithSetting(AutomationFixture fixture)
		=> fixture.AddPackage(
			"media",
			AutomationManifests.Package(
				AutomationManifests.DefaultPackageId,
				AutomationManifests.Action("convert", "convert.py", extraProperties: EnumSetting)),
			("convert.py", "pass"));

	private static void Configure(MemoryStateStore store, string preset)
		=> store.ConfigureSettings(
			"hoa.media/convert",
			ImmutableDictionary<string, AutomationSettingValue>.Empty
				.Add("preset", new(AutomationSettingValueKind.String, StringValue: preset)));

	private static ValueTask<IAutomationSession> OpenAsync(AutomationFixture fixture, MemoryStateStore store)
		=> AutomationModule.OpenAsync(fixture.Options, store, new AcceptingTrustConsent(), new RecordingResultRouter());

	private static AutomationSettingSchema SettingOf(IAutomationSession session)
		=> session.Snapshot.Packages.Single().Actions.Single().Settings.Single();
}
