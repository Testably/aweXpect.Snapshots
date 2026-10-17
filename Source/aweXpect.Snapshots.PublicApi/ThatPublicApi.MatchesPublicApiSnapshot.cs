using System.Reflection;
using System.Runtime.CompilerServices;
using aweXpect.Core;
using aweXpect.Core.Extending;
using aweXpect.Results;
using aweXpect.Snapshots;

namespace aweXpect;

/// <summary>
///     Public API snapshot expectations.
/// </summary>
public static class ThatPublicApi
{
	/// <summary>
	///     Verifies that the public API of the assembly matches the snapshot
	///     <c>&lt;Class&gt;.&lt;Method&gt;.awexpect-snapshot.txt</c> next to the test file.
	/// </summary>
	/// <remarks>
	///     The public API is written as C# declarations by <see cref="PublicApiWriter.Write" />. It usually differs
	///     between the target frameworks of a library, so keep a snapshot per target framework of the test project with
	///     <c>.UniqueFor.TargetFramework()</c>; each target framework of the test project loads the matching build of the
	///     library.<br />
	///     Otherwise it behaves like <c>MatchesSnapshot()</c> for strings, e.g. for the files and accepting changes.
	/// </remarks>
	/// <param name="subject">The subject.</param>
	/// <param name="callerFilePath">The path of the test file, which is set by the compiler.</param>
	/// <param name="callerMemberName">The name of the test method, which is set by the compiler.</param>
	[GuaranteesNotNull]
	public static SnapshotResult<string?, IThat<string?>> MatchesPublicApiSnapshot(
		this IThat<Assembly?> subject,
		[CallerFilePath] string callerFilePath = "",
		[CallerMemberName] string callerMemberName = "")
	{
		SnapshotResult<string?, IThat<string?>>? result = null;
		subject.Get().ExpectationBuilder
			.ForMember(MemberAccessor<Assembly?, string?>.FromFunc(
					assembly => assembly is null ? null : PublicApiWriter.Write(assembly), "public API"),
				(_, stringBuilder) => stringBuilder.Append("has a public API which "))
			.AddExpectations(expectationBuilder => result =
				new ThatSubject<string?>(expectationBuilder).MatchesSnapshot(callerFilePath, callerMemberName));
		return result!;
	}
}
