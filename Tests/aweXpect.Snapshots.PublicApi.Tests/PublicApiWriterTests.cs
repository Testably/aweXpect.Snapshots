using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using aweXpect.Snapshots.PublicApi.Fixture;
using Microsoft.CodeAnalysis;
using static aweXpect.Snapshots.PublicApi.Tests.TestHelpers.Compiler;

namespace aweXpect.Snapshots.PublicApi.Tests;

public sealed class PublicApiWriterTests
{
	private const string LibrarySource = """
	                                     namespace Library;
	                                     public class Api
	                                     {
	                                         public void Enter(Dependency.Gate gate) { }
	                                         private void Hidden() { }
	                                     }
	                                     """;

	private const string DependencySource = "namespace Dependency; public class Gate { }";

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
	public async Task Write_WhenAReferenceIsMissing_ShouldThrowInvalidOperationException()
	{
		byte[] dependency = Compile("Dependency", "2.0.0.0", DependencySource);
		string library = WriteFile("Library.dll", Compile("Library", "1.0.0.0", LibrarySource,
			[MetadataReference.CreateFromImage(dependency),]));

		void Act()
			=> PublicApiWriter.Write(library);

		await That(Act).Throws<InvalidOperationException>()
			.WithMessage(
				"The public API of 'Library.dll' cannot be written, because its references 'Dependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null' could not be resolved.*")
			.AsWildcard();
	}

	[Fact]
	public async Task Write_WhenAReferenceIsOnlyFoundInALowerVersion_ShouldThrowInvalidOperationException()
	{
		byte[] dependency = Compile("Dependency", "2.0.0.0", DependencySource);
		string library = WriteFile("Library.dll", Compile("Library", "1.0.0.0", LibrarySource,
			[MetadataReference.CreateFromImage(dependency),]));
		WriteFile("Dependency.dll", Compile("Dependency", "1.0.0.0", DependencySource),
			Path.GetDirectoryName(library));

		void Act()
			=> PublicApiWriter.Write(library);

		await That(Act).Throws<InvalidOperationException>()
			.WithMessage(
				"*'Dependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null (only version 1.0.0.0 was found)'*")
			.AsWildcard()
			.Because("a lower version may lack types of the public API, e.g. an older .NET than the assembly targets");
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

	[Fact]
	public async Task Write_WhenFileIsAReferenceAssembly_ShouldWriteThePublicApiOfTheImplementation()
	{
		byte[] dependency = Compile("Dependency", "1.0.0.0", DependencySource);
		MetadataReference[] references = [MetadataReference.CreateFromImage(dependency),];
		string implementation = WriteFile("Library.dll", Compile("Library", "1.0.0.0", LibrarySource, references));
		WriteFile("Dependency.dll", dependency, Path.GetDirectoryName(implementation));
		string reference = WriteFile("Library.dll", Compile("Library", "1.0.0.0", LibrarySource, references, true));
		WriteFile("Dependency.dll", dependency, Path.GetDirectoryName(reference));

		string result = PublicApiWriter.Write(reference);

		await That(result).IsEqualTo(PublicApiWriter.Write(implementation))
			.Because("a reference assembly, which cannot be loaded, has the same public API as its implementation");
	}

	[Fact]
	public async Task Write_WhenGivenAFile_ShouldNotLoadTheAssembly()
	{
		string name = $"Unloaded{Guid.NewGuid():N}";
		string file = WriteFile($"{name}.dll", Compile(name, "1.0.0.0", "namespace Unloaded; public class Api { }"));

		PublicApiWriter.Write(file);

		await That(AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name))
			.DoesNotContain(name);
	}

	[Fact]
	public async Task Write_WhenGivenTheFileOfAnAssembly_ShouldWriteTheSamePublicApiAsForTheAssembly()
	{
		Assembly assembly = typeof(Album).Assembly;

		string result = PublicApiWriter.Write(assembly.Location);

		await That(result).IsEqualTo(PublicApiWriter.Write(assembly));
	}
}
