using System.Reflection;
using System.Reflection.Emit;
using aweXpect.Snapshots;
using aweXpect.Snapshots.PublicApi.Fixture;

namespace aweXpect.Snapshots.PublicApi.Tests;

public sealed class ThatPublicApiTests
{
	[Fact]
	public async Task MatchesPublicApiSnapshot_ShouldDescribeThePublicApiOfTheFixture()
		=> await That(typeof(Album).Assembly).MatchesPublicApiSnapshot().UniqueFor.TargetFramework();

	[Fact]
	public async Task MatchesPublicApiSnapshot_WhenSubjectIsNull_ShouldFail()
	{
		Assembly? subject = null;

		async Task Act()
			=> await That(subject).MatchesPublicApiSnapshot();

		await That(Act).Throws<XunitException>()
			.WithMessage("""
			             Expected that subject
			             has a public API which matches the snapshot "*",
			             but it was <null>
			             """).AsWildcard().IgnoringNewlineStyle();
	}

	[Fact]
	public async Task Write_WhenAssemblyIsNotLoadedFromAFile_ShouldThrowArgumentException()
	{
		AssemblyBuilder assembly =
			AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("InMemory"), AssemblyBuilderAccess.Run);

		void Act()
			=> PublicApiWriter.Write(assembly);

		await That(Act).Throws<ArgumentException>()
			.WithMessage("The assembly 'InMemory' was not loaded from a file, so its public API cannot be read.*")
			.AsWildcard();
	}
}
