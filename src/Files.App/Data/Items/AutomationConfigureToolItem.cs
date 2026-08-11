// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One external tool's path box on the Configure dialog's Programs page.
	/// </summary>
	/// <remarks>
	/// Validation runs on every keystroke through <see cref="AutomationExternalToolPathRules"/> — the same rule the
	/// session screens a submitted path with, so a path this accepts cannot be refused on Save. That evaluation is
	/// the only filesystem touch in the whole dialog.
	/// </remarks>
	public sealed partial class AutomationConfigureToolItem : ObservableObject
	{
		private readonly Action _changed;

		private string _path;

		public AutomationConfigureToolItem(AutomationExternalToolSchema schema, Action changed)
		{
			ArgumentNullException.ThrowIfNull(schema);
			ArgumentNullException.ThrowIfNull(changed);

			Id = schema.Id;
			DisplayName = schema.DisplayName;
			ConfiguredPath = schema.ConfiguredPath;
			_path = schema.ConfiguredPath;
			_changed = changed;
		}

		public string Id { get; }

		public string DisplayName { get; }

		/// <summary>
		/// Gets the path this tool had when the dialog opened, so Cancel needs no rollback and Save can tell what
		/// the user actually touched.
		/// </summary>
		public string ConfiguredPath { get; }

		public string Path
		{
			get => _path;
			set
			{
				if (!SetProperty(ref _path, value))
					return;

				OnPropertyChanged(nameof(IsAcceptable));
				OnPropertyChanged(nameof(HasMessage));
				OnPropertyChanged(nameof(Message));
				_changed();
			}
		}

		/// <summary>
		/// Gets whether Save may proceed with this box as it stands. An empty box is acceptable and means the tool
		/// is left unconfigured, or cleared if it was configured before — which is the one spelling clearing has.
		/// </summary>
		public bool IsAcceptable
			=> string.IsNullOrWhiteSpace(Path) ||
				AutomationExternalToolPathRules.Evaluate(Path).Verdict is AutomationToolPathVerdict.Valid;

		/// <summary>
		/// Gets whether this box is filled in with something that cannot be used, which is what marks its rail entry.
		/// </summary>
		public bool IsRefused => !IsAcceptable;

		public bool HasMessage => !string.IsNullOrWhiteSpace(Path);

		/// <summary>
		/// Gets the confirmation or the refusal shown directly beneath the box, or an empty string while it is
		/// empty — an untouched box is not an error to shout about.
		/// </summary>
		public string Message
			=> string.IsNullOrWhiteSpace(Path)
				? string.Empty
				: AutomationExternalToolPathRules.Evaluate(Path).ToLabel();

		/// <summary>
		/// Gets whether this tool was submitted, in the sense the write path means: a box left exactly as it opened
		/// is not mentioned at all, so re-saving a dialog nobody edited writes nothing.
		/// </summary>
		public bool IsChanged => !string.Equals(Path, ConfiguredPath, StringComparison.Ordinal);

		/// <summary>
		/// Gets the path a sibling tool's folder offers for this one, or <see langword="null"/> when there is none.
		/// </summary>
		public string? Suggestion { get; private set; }

		public bool HasSuggestion => Suggestion is not null;

		/// <summary>
		/// Gets the link's text, which names the program rather than the whole path — the path is what appears in
		/// the box when it is followed, and showing it twice says nothing extra.
		/// </summary>
		public string SuggestionLabel
			=> Suggestion is null
				? string.Empty
				: string.Format(
					Strings.AutomationConfigureUseSibling.GetLocalizedResource(),
					SystemIO.Path.GetFileName(Suggestion),
					SystemIO.Path.GetFileName(SystemIO.Path.GetDirectoryName(Suggestion)) ?? string.Empty);

		public void Suggest(string? suggestion)
		{
			if (string.Equals(Suggestion, suggestion, StringComparison.OrdinalIgnoreCase))
				return;

			Suggestion = suggestion;
			OnPropertyChanged(nameof(Suggestion));
			OnPropertyChanged(nameof(HasSuggestion));
			OnPropertyChanged(nameof(SuggestionLabel));
		}

		/// <summary>
		/// Types the suggestion into the box rather than applying it. The user sees the value and it validates the
		/// way anything they entered does.
		/// </summary>
		[RelayCommand]
		private void UseSuggestion()
		{
			if (Suggestion is { } suggestion)
				Path = suggestion;
		}

		[RelayCommand]
		private async Task BrowseAsync()
		{
			var picker = new Windows.Storage.Pickers.FileOpenPicker
			{
				SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
			};
			picker.FileTypeFilter.Add(".exe");
			WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);

			// A cancelled picker leaves the box alone rather than clearing it, which would read as the picker
			// having chosen "nothing".
			if (await picker.PickSingleFileAsync() is { } file)
				Path = file.Path;
		}
	}
}
