// Copyright (c) Files Community
// Licensed under the MIT License.

namespace VxFiles.Automation.Abstractions;

/// <summary>
/// Why a configured external-tool path can or cannot be used.
/// </summary>
/// <remarks>
/// A verdict rather than a message, on the <see cref="AutomationSelectionRules"/> precedent: the session turns it
/// into exception text and a host surface turns it into localized text, so returning a string here would put
/// developer English into a dialog.
///
/// <para>
/// The <c>Target</c> members exist because a configured path may be a link, which is how winget and scoop install
/// the tools people actually have. A link leading nowhere is a different thing to tell the user than a path that
/// is not there: the first says an install moved, the second says the path was never right.
/// </para>
/// </remarks>
public enum AutomationToolPathVerdict
{
	Valid,
	NotAbsolute,
	Malformed,
	NotAnExecutable,
	NotFound,
	Unresolvable,
	TargetNotFound,
	TargetNotAnExecutable,
}

/// <summary>
/// A verdict on a configured path, together with the path that verdict is about.
/// </summary>
/// <remarks>
/// <paramref name="EffectivePath"/> is what the configured path leads to once links are followed, and the
/// configured path itself when it is not a link. A caller that accepts the verdict gets the file it should hash
/// and version without walking the link a second time; one that refuses it can name what went missing.
/// </remarks>
public sealed record AutomationToolPathEvaluation(
	AutomationToolPathVerdict Verdict,
	string EffectivePath);

/// <summary>
/// Decides whether a configured external-tool path names a program that can be run.
/// </summary>
/// <remarks>
/// Three callers, one rule. The configuration dialog calls it as the user types, the session calls it when a
/// configuration is applied — a rule the caller may skip is not a rule — and <c>AutomationDependencyResolver</c>
/// calls it on every run. The last is not made redundant by the second: a path valid on Tuesday can be gone on
/// Wednesday.
///
/// <para>
/// Alone among the rules in this assembly it touches the disk, because the question it answers is partly about
/// what is there. It is two stat calls and a link hop, which is what makes it affordable to ask on a keystroke.
/// </para>
///
/// <para>
/// Distinct from the internal <c>AutomationExternalToolRules</c>, which validates what a manifest declares rather
/// than what a user configured.
/// </para>
/// </remarks>
public static class AutomationExternalToolPathRules
{
	/// <summary>
	/// Reports the first reason <paramref name="executablePath"/> cannot be run, or
	/// <see cref="AutomationToolPathVerdict.Valid"/> when it names a program that is there.
	/// </summary>
	public static AutomationToolPathEvaluation Evaluate(string executablePath)
	{
		ArgumentNullException.ThrowIfNull(executablePath);

		// Typed live into a text box, so a string naming no location at all can arrive here. That is a verdict to
		// render, not an exception the caller has to catch on a keystroke.
		if (!Path.IsPathFullyQualified(executablePath))
			return new(AutomationToolPathVerdict.NotAbsolute, executablePath);

		string configuredPath;
		try
		{
			configuredPath = Path.GetFullPath(executablePath);
		}
		catch (ArgumentException)
		{
			// Only a path carrying a character no path may carry at all reaches here — an embedded null, which
			// is pasted rather than typed. Reported apart from NotAbsolute because such a path can be perfectly
			// absolute, and telling the user to enter a full path would be no help at all.
			return new(AutomationToolPathVerdict.Malformed, executablePath);
		}

		if (!HasExecutableExtension(configuredPath))
			return new(AutomationToolPathVerdict.NotAnExecutable, configuredPath);

		// False for a directory as well as for nothing at all, which is why no verdict names a directory.
		if (!File.Exists(configuredPath))
			return new(AutomationToolPathVerdict.NotFound, configuredPath);

		// Everything above was asked of the link. Ask it again of the target, because neither guard survives the
		// hop: File.Exists reports a dangling link as present, and ResolveLinkTarget hands back a target that is
		// not there rather than throwing. A link may stand in for a program; it may not stand in for nothing, nor
		// smuggle in something that is not a program.
		string path;
		try
		{
			// Null means the path was never a link; returnFinalTarget walks a chain rather than one hop.
			path = File.ResolveLinkTarget(configuredPath, returnFinalTarget: true) is { } target
				? Path.GetFullPath(target.FullName)
				: configuredPath;
		}
		catch (IOException)
		{
			return new(AutomationToolPathVerdict.Unresolvable, configuredPath);
		}

		if (!File.Exists(path))
			return new(AutomationToolPathVerdict.TargetNotFound, path);
		if (!HasExecutableExtension(path))
			return new(AutomationToolPathVerdict.TargetNotAnExecutable, path);

		return new(AutomationToolPathVerdict.Valid, path);
	}

	private static bool HasExecutableExtension(string path)
		=> string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
}
