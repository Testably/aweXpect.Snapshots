using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SymbolDisplayFormat = Microsoft.CodeAnalysis.SymbolDisplayFormat;

namespace aweXpect.Snapshots;

/// <summary>
///     Writes the public API of a compiled assembly as C# declarations.
/// </summary>
/// <remarks>
///     The assembly is read with Roslyn from its metadata, so that newer language features, e.g. extension members, are
///     described like in the source.
/// </remarks>
public static class PublicApiWriter
{
	private const string Indentation = "    ";

	private static readonly SymbolDisplayFormat MemberFormat = new(
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters |
		                 SymbolDisplayGenericsOptions.IncludeVariance,
		memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility |
		               SymbolDisplayMemberOptions.IncludeModifiers |
		               SymbolDisplayMemberOptions.IncludeType |
		               SymbolDisplayMemberOptions.IncludeParameters |
		               SymbolDisplayMemberOptions.IncludeRef |
		               SymbolDisplayMemberOptions.IncludeConstantValue |
		               SymbolDisplayMemberOptions.IncludeExplicitInterface,
		parameterOptions: SymbolDisplayParameterOptions.IncludeType |
		                  SymbolDisplayParameterOptions.IncludeName |
		                  SymbolDisplayParameterOptions.IncludeDefaultValue |
		                  SymbolDisplayParameterOptions.IncludeExtensionThis |
		                  SymbolDisplayParameterOptions.IncludeModifiers |
		                  SymbolDisplayParameterOptions.IncludeParamsRefOut,
		propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
		delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
		                      SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
		                      SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
		                      SymbolDisplayMiscellaneousOptions.UseAsterisksInMultiDimensionalArrays);

	/// <summary>
	///     Types are written without their type parameters, as the display does not include their attributes.
	/// </summary>
	private static readonly SymbolDisplayFormat TypeNameFormat = new(
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

	private static readonly SymbolDisplayFormat EventFormat =
		MemberFormat.WithKindOptions(SymbolDisplayKindOptions.IncludeMemberKeyword);

	/// <summary>
	///     Methods are written without their parameters, as the display does not include the attributes of parameters,
	///     e.g. <c>[CallerArgumentExpression(…)]</c>.
	/// </summary>
	private static readonly SymbolDisplayFormat MethodHeaderFormat = MemberFormat
		.RemoveMemberOptions(SymbolDisplayMemberOptions.IncludeParameters);

	/// <summary>
	///     Attributes that only carry information the declarations already show or that are irrelevant for consumers.
	/// </summary>
	private static readonly HashSet<string> IgnoredAttributes =
	[
		"System.Diagnostics.DebuggerHiddenAttribute",
		"System.Diagnostics.DebuggerStepThroughAttribute",
		"System.Reflection.DefaultMemberAttribute",
		"System.Runtime.CompilerServices.AsyncStateMachineAttribute",
		"System.Runtime.CompilerServices.CompilerGeneratedAttribute",
		"System.Runtime.CompilerServices.IteratorStateMachineAttribute",
		"System.Runtime.CompilerServices.NullableAttribute",
		"System.Runtime.CompilerServices.NullableContextAttribute",
		"System.Runtime.CompilerServices.ExtensionMarkerAttribute",
		"System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
		"System.Diagnostics.DebuggableAttribute",
		"System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
		"System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
		"System.Reflection.AssemblyCompanyAttribute",
		"System.Reflection.AssemblyConfigurationAttribute",
		"System.Reflection.AssemblyCopyrightAttribute",
		"System.Reflection.AssemblyDescriptionAttribute",
		"System.Reflection.AssemblyFileVersionAttribute",
		"System.Reflection.AssemblyInformationalVersionAttribute",
		"System.Reflection.AssemblyProductAttribute",
		"System.Reflection.AssemblyTitleAttribute",
	];

	private static readonly Regex AttributeSuffix = new(@"Attribute(?=\(|$)", RegexOptions.Compiled);

	/// <summary>
	///     Writes the public API of the <paramref name="assembly" />, which must be loaded from a file.
	/// </summary>
	/// <remarks>
	///     The types in signatures are resolved from the assemblies next to it, of the application, of the .NET version
	///     it targets (when the .NET SDK is installed) and of the current runtime; a type that cannot be resolved is
	///     written with its full name, but without the arguments of an attribute.
	/// </remarks>
	public static string Write(Assembly assembly)
	{
		if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
		{
			throw new ArgumentException(
				$"The assembly '{assembly.GetName().Name}' was not loaded from a file, so its public API cannot be read.",
				nameof(assembly));
		}

		return WriteFile(assembly.Location, GetReferencePack(assembly.Location));
	}

	private static string WriteFile(string assemblyFile, string? referencePack)
	{
		MetadataReference target = MetadataReference.CreateFromFile(assemblyFile);
		CSharpCompilation compilation = CSharpCompilation.Create("PublicApi",
			references: GetReferences(assemblyFile, referencePack).Append(target));
		IAssemblySymbol assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(target)!;

		StringBuilder sb = new();
		foreach (string attribute in Attributes(assembly))
		{
			sb.Append("[assembly: ").Append(attribute).Append("]\n");
		}

		foreach (IGrouping<string, INamedTypeSymbol> types in GetNamespaces(assembly.GlobalNamespace)
			         .SelectMany(ns => ns.GetTypeMembers())
			         .Where(IsVisible)
			         .GroupBy(type => type.ContainingNamespace.ToDisplayString())
			         .OrderBy(group => group.Key, StringComparer.Ordinal))
		{
			sb.Append("namespace ").Append(types.Key).Append('\n');
			sb.Append("{\n");
			foreach (INamedTypeSymbol type in types.OrderBy(type => type.Name, StringComparer.Ordinal)
				         .ThenBy(type => type.Arity))
			{
				WriteType(sb, type, Indentation);
			}

			sb.Append("}\n");
		}

		return sb.ToString();
	}

	/// <remarks>
	///     The build output of a library does not contain its dependencies, which the application, e.g. the tests,
	///     does. The .NET Framework keeps the facades, e.g. <c>netstandard.dll</c>, in a subdirectory of the runtime.
	/// </remarks>
	private static IEnumerable<MetadataReference> GetReferences(string assemblyFile, string? referencePack)
	{
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
		{
			Path.GetFileName(assemblyFile),
		};
		string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
		string[] frameworkDirectories = referencePack is null
			? [runtimeDirectory, Path.Combine(runtimeDirectory, "Facades"),]
			: [referencePack,];
		foreach (string directory in new[]
		         {
			         Path.GetDirectoryName(assemblyFile)!, AppDomain.CurrentDomain.BaseDirectory,
		         }.Concat(frameworkDirectories).Where(Directory.Exists))
		{
			foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
			{
				if (names.Add(Path.GetFileName(file)) && IsManagedAssembly(file))
				{
					yield return MetadataReference.CreateFromFile(file);
				}
			}
		}
	}

	/// <summary>
	///     Returns the reference assemblies of the .NET version that the <paramref name="assemblyFile" /> targets, which the
	///     .NET SDK installs next to the runtimes, so that the types of a newer .NET than the current runtime are
	///     resolved.
	/// </summary>
	private static string? GetReferencePack(string assemblyFile)
	{
		const string prefix = ".NETCoreApp,Version=v";
		string? targetFramework = ReadTargetFramework(assemblyFile);
		if (targetFramework is null || !targetFramework.StartsWith(prefix, StringComparison.Ordinal))
		{
			return null;
		}

		string version = targetFramework.Substring(prefix.Length);
		string packs = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),
			"..", "..", "..", "packs", "Microsoft.NETCore.App.Ref"));
		if (!Directory.Exists(packs))
		{
			return null;
		}

		return Directory.EnumerateDirectories(packs, $"{version}.*")
			.Select(directory => (Directory: directory,
				Version: Version.TryParse(Path.GetFileName(directory), out Version? packVersion) ? packVersion : null))
			.Where(pack => pack.Version is not null)
			.OrderByDescending(pack => pack.Version)
			.Select(pack => Path.Combine(pack.Directory, "ref", $"net{version}"))
			.FirstOrDefault(Directory.Exists);
	}

	/// <remarks>
	///     The attribute is read from the metadata of the file, as loading its type fails for an assembly that targets a
	///     newer .NET than the current runtime.
	/// </remarks>
	private static string? ReadTargetFramework(string assemblyFile)
	{
		using FileStream stream = File.OpenRead(assemblyFile);
		using PEReader peReader = new(stream);
		MetadataReader reader = peReader.GetMetadataReader();
		foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
		{
			CustomAttribute attribute = reader.GetCustomAttribute(handle);
			if (attribute.Constructor.Kind != HandleKind.MemberReference)
			{
				continue;
			}

			EntityHandle parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
			if (parent.Kind != HandleKind.TypeReference)
			{
				continue;
			}

			TypeReference type = reader.GetTypeReference((TypeReferenceHandle)parent);
			if (reader.StringComparer.Equals(type.Namespace, "System.Runtime.Versioning") &&
			    reader.StringComparer.Equals(type.Name, "TargetFrameworkAttribute"))
			{
				BlobReader value = reader.GetBlobReader(attribute.Value);
				_ = value.ReadUInt16();
				return value.ReadSerializedString();
			}
		}

		return null;
	}

	private static bool IsManagedAssembly(string file)
	{
		using FileStream stream = File.OpenRead(file);
		using PEReader reader = new(stream);
		return reader.HasMetadata;
	}

	private static IEnumerable<INamespaceSymbol> GetNamespaces(INamespaceSymbol ns)
		=> ns.GetNamespaceMembers().SelectMany(GetNamespaces).Prepend(ns);

	private static bool IsVisible(ISymbol symbol)
		=> (symbol.CanBeReferencedByName || symbol is IPropertySymbol { IsIndexer: true, } or IMethodSymbol
		   {
			   MethodKind: MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion,
		   }) &&
		   symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
			   or Accessibility.ProtectedOrInternal;

	private static void WriteType(StringBuilder sb, INamedTypeSymbol type, string indent)
	{
		WriteAttributes(sb, type, indent);
		sb.Append(indent);
		if (type.TypeKind == TypeKind.Delegate)
		{
			IMethodSymbol invoke = type.DelegateInvokeMethod!;
			sb.Append(AccessibilityKeyword(type)).Append(" delegate ")
				.Append(invoke.ReturnType.ToDisplayString(
					MemberFormat.RemoveGenericsOptions(SymbolDisplayGenericsOptions.IncludeVariance))).Append(' ')
				.Append(type.ToDisplayString(TypeNameFormat)).Append(TypeParameterList(type.TypeParameters))
				.Append('(').Append(string.Join(", ", invoke.Parameters.Select(Parameter))).Append(')')
				.Append(Constraints(type.TypeParameters)).Append(";\n");
			return;
		}

		sb.Append(TypeHeader(type)).Append('\n');
		sb.Append(indent).Append("{\n");
		string memberIndent = indent + Indentation;
		if (type.TypeKind == TypeKind.Enum)
		{
			foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue)
				         .OrderBy(field => Convert.ToDecimal(field.ConstantValue)))
			{
				WriteAttributes(sb, field, memberIndent);
				sb.Append(memberIndent).Append(field.Name).Append(" = ").Append(field.ConstantValue).Append(",\n");
			}
		}
		else
		{
			WriteMembers(sb, type, memberIndent);
			foreach (INamedTypeSymbol extension in type.GetTypeMembers().Where(nested => nested.IsExtension)
				         .OrderBy(nested => nested.ExtensionParameter?.ToDisplayString(MemberFormat), StringComparer.Ordinal))
			{
				sb.Append(memberIndent).Append("extension").Append(TypeParameterList(extension.TypeParameters))
					.Append('(').Append(extension.ExtensionParameter!.ToDisplayString(MemberFormat)).Append(')')
					.Append(Constraints(extension.TypeParameters)).Append('\n');
				sb.Append(memberIndent).Append("{\n");
				WriteMembers(sb, extension, memberIndent + Indentation);
				sb.Append(memberIndent).Append("}\n");
			}

			foreach (INamedTypeSymbol nested in type.GetTypeMembers().Where(IsVisible)
				         .OrderBy(nested => nested.Name, StringComparer.Ordinal)
				         .ThenBy(nested => nested.Arity))
			{
				WriteType(sb, nested, memberIndent);
			}
		}

		sb.Append(indent).Append("}\n");
	}

	private static void WriteMembers(StringBuilder sb, INamedTypeSymbol type, string indent)
	{
		HashSet<ISymbol> extensionImplementations = new(type.GetTypeMembers()
			.Where(nested => nested.IsExtension)
			.SelectMany(extension => extension.GetMembers())
			.SelectMany(member => member switch
			{
				IMethodSymbol method => [method.AssociatedExtensionImplementation,],
				IPropertySymbol property =>
					new[]
					{
						property.GetMethod, property.SetMethod,
					}.Select(a => a?.AssociatedExtensionImplementation),
				_ => [],
			})
			.OfType<ISymbol>()
			.Select(implementation => implementation.OriginalDefinition), SymbolEqualityComparer.Default);

		foreach ((ISymbol Member, string Declaration) member in type.GetMembers()
			         .Where(member => member is not INamedTypeSymbol && IsVisible(member) &&
			                          member is not IMethodSymbol { AssociatedSymbol: not null, } &&
			                          !extensionImplementations.Contains(member.OriginalDefinition))
			         .Where(member => member is not IMethodSymbol
			         {
				         MethodKind: MethodKind.Constructor, Parameters.Length: 0,
			         } || !type.IsValueType)
			         .Select(member => (Member: member, Declaration: Declaration(member)))
			         .OrderBy(member => Rank(member.Member))
			         .ThenBy(member => member.Member.IsStatic)
			         .ThenBy(member => member.Member.Name, StringComparer.Ordinal)
			         .ThenBy(member => member.Declaration, StringComparer.Ordinal))
		{
			WriteAttributes(sb, member.Member, indent);
			sb.Append(indent).Append(member.Declaration).Append('\n');
		}
	}

	private static string Declaration(ISymbol member) => member switch
	{
		IMethodSymbol method => MethodHeader(method) + (method.IsExtensionMethod ? "(this " : "(") +
		                        string.Join(", ", method.Parameters.Select(Parameter)) + ")" +
		                        Constraints(method.TypeParameters) + (method.IsAbstract ? ";" : " { }"),
		IEventSymbol => member.ToDisplayString(EventFormat) + ";",
		IFieldSymbol => member.ToDisplayString(MemberFormat) + ";",
		_ => member.ToDisplayString(MemberFormat),
	};

	/// <remarks>
	///     The display ends with the type parameters of the method, which are replaced to include their attributes.
	/// </remarks>
	private static string MethodHeader(IMethodSymbol method)
	{
		string header = method.ToDisplayString(MethodHeaderFormat);
		if (method.TypeParameters.Length == 0)
		{
			return header;
		}

		string typeParameters = "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";
		return header.Substring(0, header.Length - typeParameters.Length) + TypeParameterList(method.TypeParameters);
	}

	private static string TypeParameterList(IReadOnlyCollection<ITypeParameterSymbol> typeParameters)
		=> typeParameters.Count == 0
			? ""
			: "<" + string.Join(", ", typeParameters.Select(typeParameter =>
				string.Concat(Attributes(typeParameter).Select(attribute => $"[{attribute}] ")) +
				typeParameter.Variance switch
				{
					VarianceKind.In => "in ",
					VarianceKind.Out => "out ",
					_ => "",
				} + typeParameter.Name)) + ">";

	private static string Parameter(IParameterSymbol parameter)
		=> string.Concat(Attributes(parameter).Select(attribute => $"[{attribute}] ")) +
		   parameter.ToDisplayString(MemberFormat);

	private static int Rank(ISymbol member) => member switch
	{
		IFieldSymbol => 0,
		IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor, } => 1,
		IPropertySymbol => 2,
		IEventSymbol => 3,
		IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion, } => 5,
		_ => 4,
	};

	private static string TypeHeader(INamedTypeSymbol type)
	{
		StringBuilder sb = new(AccessibilityKeyword(type));
		if (type.TypeKind == TypeKind.Class)
		{
			if (type.IsStatic)
			{
				sb.Append(" static");
			}
			else if (type.IsAbstract)
			{
				sb.Append(" abstract");
			}
			else if (type.IsSealed)
			{
				sb.Append(" sealed");
			}
		}

		sb.Append(' ').Append(TypeKeyword(type)).Append(' ').Append(type.ToDisplayString(TypeNameFormat))
			.Append(TypeParameterList(type.TypeParameters));
		List<string> baseTypes = [];
		if (type.TypeKind == TypeKind.Enum)
		{
			if (type.EnumUnderlyingType!.SpecialType != SpecialType.System_Int32)
			{
				baseTypes.Add(type.EnumUnderlyingType.ToDisplayString(MemberFormat));
			}
		}
		else
		{
			if (type.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType), })
			{
				baseTypes.Add(type.BaseType.ToDisplayString(MemberFormat));
			}

			baseTypes.AddRange(type.Interfaces.Select(i => i.ToDisplayString(MemberFormat))
				.OrderBy(i => i, StringComparer.Ordinal));
		}

		if (baseTypes.Count > 0)
		{
			sb.Append(" : ").Append(string.Join(", ", baseTypes));
		}

		return sb.Append(Constraints(type.TypeParameters)).ToString();
	}

	private static string Constraints(IEnumerable<ITypeParameterSymbol> typeParameters)
	{
		StringBuilder sb = new();
		foreach (ITypeParameterSymbol typeParameter in typeParameters)
		{
			List<string> constraints = [];
			if (typeParameter.HasReferenceTypeConstraint)
			{
				constraints.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
					? "class?"
					: "class");
			}
			else if (typeParameter.HasUnmanagedTypeConstraint)
			{
				constraints.Add("unmanaged");
			}
			else if (typeParameter.HasValueTypeConstraint)
			{
				constraints.Add("struct");
			}
			else if (typeParameter.HasNotNullConstraint)
			{
				constraints.Add("notnull");
			}

			constraints.AddRange(typeParameter.ConstraintTypes.Select(t => t.ToDisplayString(MemberFormat)));
			if (typeParameter.HasConstructorConstraint)
			{
				constraints.Add("new()");
			}

			if (typeParameter.AllowsRefLikeType)
			{
				constraints.Add("allows ref struct");
			}

			if (constraints.Count > 0)
			{
				sb.Append(" where ").Append(typeParameter.Name).Append(" : ").Append(string.Join(", ", constraints));
			}
		}

		return sb.ToString();
	}

	private static string TypeKeyword(INamedTypeSymbol type) => type.TypeKind switch
	{
		TypeKind.Interface => "interface",
		TypeKind.Enum => "enum",
		TypeKind.Struct => (type.IsReadOnly ? "readonly " : "") + (type.IsRefLikeType ? "ref " : "") + "struct",
		_ => type.IsRecord ? "record" : "class",
	};

	private static string AccessibilityKeyword(ISymbol symbol) => symbol.DeclaredAccessibility switch
	{
		Accessibility.Protected => "protected",
		Accessibility.ProtectedOrInternal => "protected internal",
		_ => "public",
	};

	private static void WriteAttributes(StringBuilder sb, ISymbol symbol, string indent)
	{
		foreach (string attribute in Attributes(symbol))
		{
			sb.Append(indent).Append('[').Append(attribute).Append("]\n");
		}
	}

	private static IEnumerable<string> Attributes(ISymbol symbol)
		=> symbol.GetAttributes()
			.Where(attribute => attribute.AttributeClass is not null &&
			                    !IgnoredAttributes.Contains(attribute.AttributeClass.ToDisplayString()) &&
			                    !IsSnapshotsMetadata(attribute))
			.Select(attribute => AttributeSuffix.Replace(attribute.ToString()!, ""))
			.OrderBy(attribute => attribute, StringComparer.Ordinal);

	/// <summary>
	///     The build targets of aweXpect.Snapshots add the source roots of a test project as assembly metadata, which
	///     depends on the machine and is no API.
	/// </summary>
	private static bool IsSnapshotsMetadata(AttributeData attribute)
		=> attribute.AttributeClass!.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
		   attribute.ConstructorArguments.Length > 0 &&
		   attribute.ConstructorArguments[0].Value is string key &&
		   key.StartsWith("aweXpect.Snapshots.", StringComparison.Ordinal);
}
