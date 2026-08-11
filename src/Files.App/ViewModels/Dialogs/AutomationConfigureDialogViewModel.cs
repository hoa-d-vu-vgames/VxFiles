// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.IO;
using VxFiles.Automation.Abstractions;

namespace Files.App.ViewModels.Dialogs
{
	/// <summary>
	/// Everything the Configure dialog edits for one Automation Package: the paths of the programs it needs, and
	/// the settings of the actions that declare any.
	/// </summary>
	/// <remarks>
	/// Built entirely from the snapshot, which already carries each declared tool's configured path and each
	/// setting's current value. The dialog therefore opens with no read of any kind, and Cancel is free rather than
	/// a rollback.
	///
	/// <para>
	/// The one filesystem touch in the whole flow is <see cref="AutomationExternalToolPathRules"/>, called as the
	/// user types. Nothing here parses a manifest, hashes anything, or starts a process; Save is one
	/// <see cref="IAutomationSession.ApplyPackageConfigurationAsync"/>.
	/// </para>
	/// </remarks>
	public sealed partial class AutomationConfigureDialogViewModel : ObservableObject
	{
		private readonly AutomationPackageSnapshot _snapshot;

		private AutomationConfigurePageItem? _selectedPage;
		private string _failure = string.Empty;

		public AutomationConfigureDialogViewModel(AutomationPackageSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

			_snapshot = snapshot;
			PackageDisplayName = snapshot.DisplayName;

			if (!snapshot.ExternalTools.IsEmpty)
			{
				Pages.Add(AutomationConfigurePageItem.Programs(
					snapshot.ExternalTools.Select(tool => new AutomationConfigureToolItem(tool, OnEdited))));
			}

			foreach (var action in snapshot.Actions.Where(action => !action.Settings.IsEmpty))
			{
				Pages.Add(AutomationConfigurePageItem.ForAction(
					action,
					action.Settings.Select(setting => new AutomationConfigureSettingItem(setting, OnEdited))));
			}

			_selectedPage = Pages.FirstOrDefault();
			RefreshSuggestions();
		}

		public string PackageDisplayName { get; }

		public ObservableCollection<AutomationConfigurePageItem> Pages { get; } = [];

		/// <summary>
		/// Gets whether this package has an external tool, and therefore whether the trust warning belongs on the
		/// page the user is looking at.
		/// </summary>
		public bool HasExternalTools => !_snapshot.ExternalTools.IsEmpty;

		public AutomationConfigurePageItem? SelectedPage
		{
			get => _selectedPage;
			set => SetProperty(ref _selectedPage, value);
		}

		/// <summary>
		/// Gets whether Save may proceed: every path box is either empty or names a program that is there.
		/// </summary>
		public bool CanSave => Pages.All(page => page.Tools.All(tool => tool.IsAcceptable));

		/// <summary>
		/// Gets why the last Save did not go through, or an empty string.
		/// </summary>
		/// <remarks>
		/// Save is disabled for everything the dialog can see coming, so anything reaching here moved underneath it
		/// — a program deleted between the last keystroke and the click, or a package folder edited while the
		/// dialog was open.
		/// </remarks>
		public string Failure
		{
			get => _failure;
			set
			{
				if (SetProperty(ref _failure, value))
					OnPropertyChanged(nameof(HasFailure));
			}
		}

		public bool HasFailure => Failure.Length is not 0;

		/// <summary>
		/// Composes what Save submits: only what the user actually moved.
		/// </summary>
		/// <remarks>
		/// A box left exactly as it opened is not mentioned, because the write path reads each dictionary per entry
		/// — a tool nobody touched keeps what it had, and one emptied is cleared. Sending everything back would
		/// work too, but it would mean re-submitting paths the user never looked at.
		/// </remarks>
		public AutomationPackageConfiguration BuildConfiguration()
		{
			var tools = ImmutableDictionary.CreateBuilder<string, AutomationExternalToolConfiguration>(StringComparer.Ordinal);
			foreach (var tool in Pages.SelectMany(page => page.Tools).Where(tool => tool.IsChanged))
				tools.Add(tool.Id, new(tool.Id, tool.Path.Trim()));

			var settings = ImmutableDictionary.CreateBuilder<AutomationActionLocalId, AutomationActionSettings>();
			foreach (var page in Pages.Where(page => page.ActionId is not null))
			{
				var changed = page.Settings.Where(setting => setting.IsChanged).ToArray();
				if (changed.Length is 0)
					continue;

				settings.Add(
					page.ActionId!.Value,
					new(changed.ToImmutableDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal)));
			}

			return new(tools.ToImmutable(), settings.ToImmutable());
		}

		/// <summary>
		/// Offers a tool that is not configured the folder a sibling tool was found in, when the program it would
		/// name is actually sitting there.
		/// </summary>
		/// <remarks>
		/// Suggested, never derived: the link types the path into the box and the user sees it validate like
		/// anything they entered. Applying it silently would configure a program they never chose, and a package's
		/// two tools are only usually shipped together.
		///
		/// <para>
		/// The file name is guessed from the tool's own id — <c>hoa.ffprobe</c> looks for <c>ffprobe.exe</c> — and
		/// the suggestion is withheld unless that file is really there, so the link cannot offer a path that
		/// immediately reports itself broken.
		/// </para>
		/// </remarks>
		private void RefreshSuggestions()
		{
			var tools = Pages.SelectMany(page => page.Tools).ToArray();
			var folders = tools
				.Where(tool => !string.IsNullOrWhiteSpace(tool.Path))
				.Select(tool => AutomationExternalToolPathRules.Evaluate(tool.Path))
				.Where(evaluation => evaluation.Verdict is AutomationToolPathVerdict.Valid)
				.Select(evaluation => Path.GetDirectoryName(evaluation.EffectivePath))
				.Where(folder => !string.IsNullOrEmpty(folder))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			foreach (var tool in tools)
			{
				var suggestion = string.IsNullOrWhiteSpace(tool.Path)
					? folders.Select(folder => Path.Join(folder, ExecutableNameFor(tool.Id))).FirstOrDefault(File.Exists)
					: null;

				tool.Suggest(suggestion);
			}
		}

		private static string ExecutableNameFor(string toolId)
		{
			var separator = toolId.LastIndexOf('.');
			return $"{(separator < 0 ? toolId : toolId[(separator + 1)..])}.exe";
		}

		private void OnEdited()
		{
			OnPropertyChanged(nameof(CanSave));
			foreach (var page in Pages)
				page.RefreshError();

			// Configuring one tool can make its sibling suggestible, and clearing one can take the offer away.
			RefreshSuggestions();
		}
	}
}
