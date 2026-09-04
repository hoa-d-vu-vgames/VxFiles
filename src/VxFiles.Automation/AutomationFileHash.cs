// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Security.Cryptography;

namespace VxFiles.Automation;

/// <summary>
/// SHA-256 over a file's contents, streamed.
/// </summary>
/// <remarks>
/// <para>
/// Every identity in this module rests on one of these, so they are computed often: a run resolves each
/// external tool and fingerprints the package and runtime trees, then fingerprints them again to catch content
/// changing while a trust prompt was open. Reading whole put each file on the large object heap for the
/// duration — around 150 MB for a static ffmpeg.exe, three times over in a single run.
/// </para>
/// <para>
/// Deliberately takes no <see cref="CancellationToken"/>, though every caller has one. A session reports
/// cancellation as a run whose state is <c>Cancelled</c>; it never throws out of <c>InvokeAsync</c>. All of this
/// hashing happens during preparation, before a run record exists to carry that state, so observing a token here
/// would turn a cancelled run into an exception escaping the session. Async for the I/O, not for the token.
/// </para>
/// </remarks>
internal static class AutomationFileHash
{
	public static async ValueTask<byte[]> ComputeAsync(string path)
	{
		await using var stream = File.OpenRead(path);
		return await SHA256.HashDataAsync(stream);
	}

	public static async ValueTask<string> ComputeHexAsync(string path)
		=> Convert.ToHexStringLower(await ComputeAsync(path));
}
