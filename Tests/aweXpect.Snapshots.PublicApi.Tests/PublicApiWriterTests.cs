using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using aweXpect.Snapshots.PublicApi.Fixture;

namespace aweXpect.Snapshots.PublicApi.Tests;

public sealed class PublicApiWriterTests
{
	[Fact]
	public async Task Write_ShouldWriteNumbersIndependentlyOfTheCurrentCulture()
	{
		Assembly assembly = typeof(Album).Assembly;
		CultureInfo culture = CultureInfo.CurrentCulture;
		string expected;
		string result;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			expected = PublicApiWriter.Write(assembly);
			CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
			result = PublicApiWriter.Write(assembly);
		}
		finally
		{
			CultureInfo.CurrentCulture = culture;
		}

		await That(result).IsEqualTo(expected)
			.Because("sv-SE writes negative numbers with the minus sign U+2212, e.g. the value of Mood.Sad");
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
