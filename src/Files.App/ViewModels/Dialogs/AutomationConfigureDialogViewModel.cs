// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
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
	/// Nothing here parses a manifest, hashes anything, starts a process, or reads beyond the path validation each
	/// program row performs. Save is one <see cref="IAutomationSession.ApplyPackageConfigurationAsync"/>.
	/// </para>
	/// </remarks>
	public sealed partial class AutomationConfigureDialogViewModel : ObservableObject
	{
		private AutomationConfigurePageItem? _selectedPage;
		private string _failure = string.Empty;

		public AutomationConfigureDialogViewModel(AutomationPackageSnapshot snapshot)
		{
			ArgumentNullException.ThrowIfNull(snapshot);

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
		}

		public string PackageDisplayName { get; }

		public ObservableCollection<AutomationConfigurePageItem> Pages { get; } = [];

		public AutomationConfigurePageItem? SelectedPage
		{
			get => _selectedPage;
			set => SetProperty(ref _selectedPage, value);
		}

		/// <summary>
		/// Gets whether Save may proceed: every path box is either empty or names a program that is there.
		/// </summary>
		public bool CanSave => Pages.All(page =>
			page.Tools.All(tool => tool.IsAcceptable) && page.Settings.All(setting => setting.IsAcceptable));

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
				if (!page.Settings.Any(setting => setting.IsChanged))
					continue;

				// Every setting the action declares, not only the edited ones. The write path merges per action
				// and rewrites that action's settings whole, so submitting one key alone would take its siblings
				// back to their manifest defaults — and the user would find that out the next time they opened
				// this dialog.
				settings.Add(
					page.ActionId!.Value,
					new(page.Settings.ToImmutableDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal)));
			}

			return new(tools.ToImmutable(), settings.ToImmutable());
		}

		private void OnEdited()
		{
			OnPropertyChanged(nameof(CanSave));
			foreach (var page in Pages)
				page.RefreshError();
		}
	}
}
