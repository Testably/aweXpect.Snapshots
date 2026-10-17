using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using aweXpect.Snapshots.PublicApi.Fixture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

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
	public async Task Write_WhenAReferenceIsMissing_ShouldThrowInvalidOperationException()
	{
		string directory = CreateDirectory();
		byte[] dependency = Compile("Dependency", "2.0.0.0", "public class Gate { }");
		File.WriteAllBytes(Path.Combine(directory, "Library.dll"), Compile("Library", "1.0.0.0",
			"public class Api { public void Enter(Gate gate) { } }", MetadataReference.CreateFromImage(dependency)));
		Assembly assembly = Assembly.LoadFile(Path.Combine(directory, "Library.dll"));

		void Act()
			=> PublicApiWriter.Write(assembly);

		await That(Act).Throws<InvalidOperationException>()
			.WithMessage(
				"The public API of 'Library.dll' cannot be written, because its references 'Dependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null' could not be resolved.*")
			.AsWildcard();
	}

	[Fact]
	public async Task Write_WhenAReferenceIsOnlyFoundInALowerVersion_ShouldThrowInvalidOperationException()
	{
		string directory = CreateDirectory();
		byte[] dependency = Compile("Dependency", "2.0.0.0", "public class Gate { }");
		File.WriteAllBytes(Path.Combine(directory, "Dependency.dll"),
			Compile("Dependency", "1.0.0.0", "public class Gate { }"));
		File.WriteAllBytes(Path.Combine(directory, "Library.dll"), Compile("Library", "1.0.0.0",
			"public class Api { public void Enter(Gate gate) { } }", MetadataReference.CreateFromImage(dependency)));
		Assembly assembly = Assembly.LoadFile(Path.Combine(directory, "Library.dll"));

		void Act()
			=> PublicApiWriter.Write(assembly);

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

	private static byte[] Compile(string name, string version, string source, params MetadataReference[] references)
	{
		CSharpCompilation compilation = CSharpCompilation.Create(name,
			[CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{version}\")]\n{source}"),],
			references.Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using MemoryStream stream = new();
		EmitResult result = compilation.Emit(stream);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
		}

		return stream.ToArray();
	}

	private static string CreateDirectory()
		=> Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "aweXpect.Snapshots.PublicApi.Tests",
			Guid.NewGuid().ToString("N"))).FullName;
}
