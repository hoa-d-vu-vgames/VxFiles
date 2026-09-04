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
		private static ICommonDialogService CommonDialogService { get; } = Ioc.Default.GetRequiredService<ICommonDialogService>();

		private readonly Action _onChanged;

		private string _path;

		public AutomationConfigureToolItem(AutomationExternalToolSchema schema, Action onChanged)
		{
			ArgumentNullException.ThrowIfNull(schema);
			ArgumentNullException.ThrowIfNull(onChanged);

			Id = schema.Id;
			DisplayName = schema.DisplayName;
			ConfiguredPath = schema.ConfiguredPath;
			_path = schema.ConfiguredPath;
			_onChanged = onChanged;
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
				OnPropertyChanged(nameof(IsRefused));
				OnPropertyChanged(nameof(IsConfirmed));
				OnPropertyChanged(nameof(Message));
				_onChanged();
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

		/// <summary>
		/// Gets whether the box holds a path that was checked and accepted, which is what earns a confirmation
		/// rather than silence.
		/// </summary>
		public bool IsConfirmed => !string.IsNullOrWhiteSpace(Path) && IsAcceptable;

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

		[RelayCommand]
		private void Browse()
		{
			// A cancelled picker leaves the box alone rather than clearing it, which would read as the picker
			// having chosen "nothing".
			if (CommonDialogService.Open_FileOpenDialog(
				MainWindow.Instance.WindowHandle,
				false,
				["*.exe"],
				Environment.SpecialFolder.ProgramFiles,
				out var path))
			{
				Path = path;
			}
		}
	}
}
