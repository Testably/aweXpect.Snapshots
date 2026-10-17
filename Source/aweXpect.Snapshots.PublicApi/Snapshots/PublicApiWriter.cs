using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
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
		"System.Runtime.CompilerServices.ReferenceAssemblyAttribute",
		"System.Reflection.AssemblyCompanyAttribute",
		"System.Reflection.AssemblyConfigurationAttribute",
		"System.Reflection.AssemblyCopyrightAttribute",
		"System.Reflection.AssemblyDescriptionAttribute",
		"System.Reflection.AssemblyFileVersionAttribute",
		"System.Reflection.AssemblyInformationalVersionAttribute",
		"System.Reflection.AssemblyProductAttribute",
		"System.Reflection.AssemblyTitleAttribute",
	];

	/// <summary>
	///     The references of the files that were read, which are <see langword="null" /> for files without an assembly,
	///     e.g. native ones, so that each file is read only once per process.
	/// </summary>
	private static readonly ConcurrentDictionary<string, (Version Version, MetadataReference Reference)?> References =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Writes the public API of the <paramref name="assembly" />, which must be loaded from a file.
	/// </summary>
	/// <remarks>
	///     The file of the assembly is read like by <see cref="Write(string)" />.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	///     An assembly that the <paramref name="assembly" /> references cannot be resolved, or only in a lower version, which
	///     would change the written public API.
	/// </exception>
	public static string Write(Assembly assembly)
	{
		if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
		{
			throw new ArgumentException(
				$"The assembly '{assembly.GetName().Name}' was not loaded from a file, so its public API cannot be read.",
				nameof(assembly));
		}

		return Write(assembly.Location);
	}

	/// <summary>
	///     Writes the public API of the assembly in the <paramref name="assemblyFile" /> without loading it, e.g. of a
	///     build for a target framework that the application cannot run on or of a reference assembly.
	/// </summary>
	/// <remarks>
	///     The types in signatures are resolved from the assemblies next to it, of the application, of the .NET version
	///     it targets (when the .NET SDK is installed) and of the current runtime.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	///     An assembly that the assembly references cannot be resolved, or only in a lower version, which would change the
	///     written public API.
	/// </exception>
	public static string Write(string assemblyFile)
	{
		string fullPath = Path.GetFullPath(assemblyFile);
		return WriteFile(fullPath, GetReferencePack(fullPath));
	}

	private static string WriteFile(string assemblyFile, string? referencePack)
	{
		MetadataReference target = MetadataReference.CreateFromFile(assemblyFile);
		CSharpCompilation compilation = CSharpCompilation.Create("PublicApi",
			references: GetReferences(assemblyFile, referencePack).Append(target));
		IAssemblySymbol assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(target)!;
		ThrowIfReferencesAreUnresolved(assemblyFile, compilation, assembly);

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
	///     Roslyn binds a reference to a lower version when no other is found, e.g. the newer .NET of the assembly to the
	///     current runtime when its reference pack is missing.
	/// </remarks>
	private static void ThrowIfReferencesAreUnresolved(string assemblyFile, Compilation compilation,
		IAssemblySymbol assembly)
	{
		HashSet<IAssemblySymbol> resolved = new(compilation.References
			.Select(compilation.GetAssemblyOrModuleSymbol)
			.OfType<IAssemblySymbol>(), SymbolEqualityComparer.Default);
		string[] unresolved = assembly.Modules
			.SelectMany(module => module.ReferencedAssemblies.Zip(module.ReferencedAssemblySymbols,
				(identity, symbol) => (Identity: identity, Symbol: symbol)))
			.Where(reference => !resolved.Contains(reference.Symbol) ||
			                    reference.Symbol.Identity.Version < reference.Identity.Version)
			.Select(reference => resolved.Contains(reference.Symbol)
				? $"{reference.Identity.GetDisplayName()} (only version {reference.Symbol.Identity.Version} was found)"
				: reference.Identity.GetDisplayName())
			.ToArray();
		if (unresolved.Length > 0)
		{
			throw new InvalidOperationException(
				$"The public API of '{Path.GetFileName(assemblyFile)}' cannot be written, because its references " +
				$"{string.Join(", ", unresolved.Select(reference => $"'{reference}'"))} could not be resolved. " +
				"Copy them next to the assembly or into the output of the tests, or install the .NET SDK of its target framework.");
		}
	}

	/// <remarks>
	///     The build output of a library does not contain its dependencies, which the application, e.g. the tests,
	///     does. The .NET Framework keeps the facades, e.g. <c>netstandard.dll</c>, in a subdirectory of the runtime.<br />
	///     Of the files with the same name, the highest version is used, as the application can contain an older package
	///     of an assembly that is part of the newer .NET the assembly targets, e.g. <c>System.Reflection.Metadata</c>.
	/// </remarks>
	private static IEnumerable<MetadataReference> GetReferences(string assemblyFile, string? referencePack)
	{
		string assemblyName = Path.GetFileName(assemblyFile);
		string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
		string[] frameworkDirectories = referencePack is null
			? [runtimeDirectory, Path.Combine(runtimeDirectory, "Facades"),]
			: [referencePack,];
		return new[]
			{
				Path.GetDirectoryName(assemblyFile)!, AppDomain.CurrentDomain.BaseDirectory,
			}.Concat(frameworkDirectories)
			.Where(Directory.Exists)
			.SelectMany(directory => Directory.EnumerateFiles(directory, "*.dll"))
			.Where(file => !string.Equals(Path.GetFileName(file), assemblyName, StringComparison.OrdinalIgnoreCase))
			.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
			.Select(files => files
				.Select(file => References.GetOrAdd(file, ReadReference))
				.OfType<(Version Version, MetadataReference Reference)>()
				.OrderByDescending(reference => reference.Version)
				.Select(reference => reference.Reference)
				.FirstOrDefault())
			.OfType<MetadataReference>();
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

	private static (Version Version, MetadataReference Reference)? ReadReference(string file)
	{
		using FileStream stream = File.OpenRead(file);
		using PEReader reader = new(stream);
		if (!reader.HasMetadata || reader.GetMetadataReader() is not { IsAssembly: true, } metadata)
		{
			return null;
		}

		return (metadata.GetAssemblyDefinition().Version, MetadataReference.CreateFromFile(file));
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
				.Append(invoke.RefKind switch
				{
					RefKind.Ref => "ref ",
					RefKind.RefReadOnly => "ref readonly ",
					_ => "",
				})
				.Append(invoke.ReturnType.ToDisplayString(
					MemberFormat.RemoveGenericsOptions(SymbolDisplayGenericsOptions.IncludeVariance))).Append(' ')
				.Append(type.ToDisplayString(TypeNameFormat)).Append(TypeParameterList(type.TypeParameters))
				.Append('(').Append(string.Join(", ", invoke.Parameters.Select(parameter => Parameter(parameter, false))))
				.Append(')')
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
				sb.Append(memberIndent).Append(field.Name).Append(" = ")
					.Append(Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)).Append(",\n");
			}
		}
		else
		{
			WriteMembers(sb, type, memberIndent);
			foreach (INamedTypeSymbol extension in type.GetTypeMembers()
				         .Where(nested => nested.IsExtension && nested.GetMembers().Any(IsVisible))
				         .OrderBy(nested => nested.ExtensionParameter?.ToDisplayString(MemberFormat), StringComparer.Ordinal))
			{
				sb.Append(memberIndent).Append("extension").Append(TypeParameterList(extension.TypeParameters))
					.Append('(').Append(Parameter(extension.ExtensionParameter!, false)).Append(')')
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
				         MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: true,
			         })
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

	private static string Declaration(ISymbol member) => InterfaceModifiers(member) + member switch
	{
		IMethodSymbol method => MethodHeader(method) + "(" +
		                        string.Join(", ", method.Parameters.Select((parameter, index)
			                        => Parameter(parameter, index == 0 && method.IsExtensionMethod))) + ")" +
		                        Constraints(method.TypeParameters) + (method.IsAbstract ? ";" : " { }"),
		IPropertySymbol property => Property(property),
		IEventSymbol => member.ToDisplayString(EventFormat) + ";",
		IFieldSymbol => member.ToDisplayString(MemberFormat) + ";",
		_ => member.ToDisplayString(MemberFormat),
	};

	/// <summary>
	///     The display omits the modifiers of interface members.
	/// </summary>
	private static string InterfaceModifiers(ISymbol member) => member switch
	{
		{ ContainingType.TypeKind: not TypeKind.Interface, } or { IsStatic: false, } => "",
		{ IsAbstract: true, } => "static abstract ",
		{ IsVirtual: true, } => "static virtual ",
		_ => "static ",
	};

	/// <summary>
	///     The display does not include the attributes of the accessors, which are inserted before their keywords.
	/// </summary>
	private static string Property(IPropertySymbol property)
	{
		string declaration = property.ToDisplayString(MemberFormat);
		int accessorsStart = declaration.LastIndexOf(" { ", StringComparison.Ordinal);
		IEnumerable<string> accessors = declaration
			.Substring(accessorsStart + 3, declaration.Length - accessorsStart - 5)
			.Split([';',], StringSplitOptions.RemoveEmptyEntries)
			.Select(accessor => accessor.Trim())
			.Select(accessor => string.Concat(MethodAttributes(accessor.EndsWith("get", StringComparison.Ordinal)
				                    ? property.GetMethod!
				                    : property.SetMethod!).Select(attribute => $"[{attribute}] ")) +
			                    accessor + ";");
		return declaration.Remove(accessorsStart) + " { " + string.Join(" ", accessors) + " }";
	}

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
		return header.Remove(header.Length - typeParameters.Length) + TypeParameterList(method.TypeParameters);
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

	private static string Parameter(IParameterSymbol parameter, bool isThis)
		=> string.Concat(Attributes(parameter).Select(attribute => $"[{attribute}] ")) +
		   (isThis ? "this " : "") + QualifyEnumDefaultValue(parameter, parameter.ToDisplayString(MemberFormat));

	/// <summary>
	///     The display writes the members of an enum in a default value without their type, e.g. <c>Rock</c>.
	/// </summary>
	private static string QualifyEnumDefaultValue(IParameterSymbol parameter, string declaration)
	{
		ITypeSymbol valueType = parameter.Type is INamedTypeSymbol
		{
			OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
		} nullable
			? nullable.TypeArguments[0]
			: parameter.Type;
		if (valueType is not INamedTypeSymbol { TypeKind: TypeKind.Enum, } type ||
		    !parameter.HasExplicitDefaultValue || parameter.ExplicitDefaultValue is null)
		{
			return declaration;
		}

		int valueStart = declaration.LastIndexOf(" = ", StringComparison.Ordinal) + 3;
		string typeName = type.ToDisplayString(MemberFormat);
		return declaration.Remove(valueStart) + string.Join(" | ", declaration.Substring(valueStart)
			.Split([" | ",], StringSplitOptions.None)
			.Select(name => type.MemberNames.Contains(name) ? $"{typeName}.{name}" : name));
	}

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
		foreach (string attribute in symbol switch
		         {
			         IMethodSymbol method => MethodAttributes(method),
			         INamedTypeSymbol { DelegateInvokeMethod: { } invoke, } => Attributes(symbol)
				         .Concat(ReturnAttributes(invoke)),
			         _ => Attributes(symbol),
		         })
		{
			sb.Append(indent).Append('[').Append(attribute).Append("]\n");
		}
	}

	private static IEnumerable<string> MethodAttributes(IMethodSymbol method)
		=> Attributes(method).Concat(ReturnAttributes(method));

	private static IEnumerable<string> ReturnAttributes(IMethodSymbol method)
		=> Attributes(method.GetReturnTypeAttributes()).Select(attribute => $"return: {attribute}");

	private static IEnumerable<string> Attributes(ISymbol symbol)
		=> Attributes(symbol.GetAttributes());

	private static IEnumerable<string> Attributes(IEnumerable<AttributeData> attributes)
		=> attributes
			.Where(attribute => attribute.AttributeClass is not null &&
			                    !IgnoredAttributes.Contains(attribute.AttributeClass.ToDisplayString()) &&
			                    !IsSnapshotsMetadata(attribute))
			.Select(AttributeText)
			.OrderBy(attribute => attribute, StringComparer.Ordinal);

	/// <remarks>
	///     The attribute is written from its arguments, as its display is no documented format.
	/// </remarks>
	private static string AttributeText(AttributeData attribute)
	{
		const string suffix = "Attribute";
		INamedTypeSymbol attributeClass = attribute.AttributeClass!;
		string name = attributeClass.ToDisplayString(MemberFormat);
		if (attributeClass.Name.EndsWith(suffix, StringComparison.Ordinal))
		{
			int nameEnd = attributeClass.IsGenericType ? name.IndexOf('<') : name.Length;
			name = name.Remove(nameEnd - suffix.Length, suffix.Length);
		}

		string arguments = string.Join(", ", attribute.ConstructorArguments.Select(Value)
			.Concat(attribute.NamedArguments.Select(argument => $"{argument.Key} = {Value(argument.Value)}")));
		return arguments.Length == 0 ? name : $"{name}({arguments})";
	}

	/// <remarks>
	///     <c>ToCSharpString()</c> writes an array without its type, e.g. <c>{1, 2}</c>.
	/// </remarks>
	private static string Value(TypedConstant constant)
		=> constant is { Kind: TypedConstantKind.Array, IsNull: false, }
			? $"new {constant.Type!.ToDisplayString(MemberFormat)} {{ {string.Join(", ", constant.Values.Select(Value))} }}"
			: constant.ToCSharpString();

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
