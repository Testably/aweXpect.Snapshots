using System.Collections.Generic;

namespace aweXpect.Snapshots.Api.Tests;

/// <summary>
///     Whenever a test fails, this means that the public API surface changed.
///     If the change was intentional, rename the pending snapshot next to this file or run the tests with the
///     environment variable <c>AWEXPECT_ACCEPT_SNAPSHOTS</c> set to <c>all</c> to take over the current public API
///     surface. The changes will become part of the pull request and will be reviewed accordingly.
/// </summary>
public sealed class ApiApprovalTests
{
	[TestCaseSource(nameof(TargetFrameworksTheoryData))]
	public async Task VerifyPublicApiForAweXpectSnapshots(string framework)
		=> await That(Helper.GetAssemblyFile(framework, "aweXpect.Snapshots"))
			.MatchesPublicApiSnapshot().Named(framework);

	[TestCaseSource(nameof(TargetFrameworksTheoryData))]
	public async Task VerifyPublicApiForAweXpectSnapshotsPublicApi(string framework)
		=> await That(Helper.GetAssemblyFile(framework, "aweXpect.Snapshots.PublicApi"))
			.MatchesPublicApiSnapshot().Named(framework);

	private static List<string> TargetFrameworksTheoryData()
	{
		List<string> theoryData = new();
		foreach (string targetFramework in Helper.GetTargetFrameworks())
		{
			theoryData.Add(targetFramework);
		}

		return theoryData;
	}
}
