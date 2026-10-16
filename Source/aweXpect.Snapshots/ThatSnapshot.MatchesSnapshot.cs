using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using aweXpect.Core;
using aweXpect.Core.Constraints;
using aweXpect.Core.Extending;
using aweXpect.Options;
using aweXpect.Results;

namespace aweXpect;

/// <summary>
///     Snapshot expectations.
/// </summary>
public static class ThatSnapshot
{
	/// <summary>
	///     Verifies that the subject matches the snapshot <c>&lt;Class&gt;.&lt;Method&gt;.snapshot.txt</c> next to the
	///     test file.
	/// </summary>
	/// <remarks>
	///     <c>&lt;Class&gt;</c> is the name of the test file without its extension and <c>&lt;Method&gt;</c> the name
	///     of the calling member. The file name is known at compile time, so no stack walk or reflection is needed,
	///     which also holds under Native AOT and in async methods, and it is unique within its directory, which a class
	///     name is not across namespaces.<br />
	///     A missing or different snapshot fails the expectation and writes the subject to
	///     <c>&lt;Class&gt;.&lt;Method&gt;.snapshot.pending.txt</c>, which can be renamed to accept it; a matching
	///     snapshot deletes a leftover pending file.<br />
	///     The files are encoded as UTF-8 and the pending file ends with an additional line break, which is removed
	///     again when the snapshot is read, so that an editor which adds a final line break does not change it.<br />
	///     The newline style is ignored, because version control can change it when the snapshot is checked out.
	/// </remarks>
	/// <param name="subject">The subject.</param>
	/// <param name="callerFilePath">The path of the test file, which is set by the compiler.</param>
	/// <param name="callerMemberName">The name of the test method, which is set by the compiler.</param>
	[GuaranteesNotNull]
	public static AndOrResult<string?, IThat<string?>> MatchesSnapshot(
		this IThat<string?> subject,
		[CallerFilePath] string callerFilePath = "",
		[CallerMemberName] string callerMemberName = "")
	{
		string basePath = Path.Combine(
			Path.GetDirectoryName(callerFilePath) ?? "",
			$"{Path.GetFileNameWithoutExtension(callerFilePath)}.{callerMemberName}");
		return new AndOrResult<string?, IThat<string?>>(
			subject.Get().ExpectationBuilder.AddConstraint(basePath,
				static (path, it, grammars) => new MatchesSnapshotConstraint(it, grammars,
					path + ".snapshot.txt", path + ".snapshot.pending.txt")),
			subject);
	}

	private sealed class MatchesSnapshotConstraint(
		string it,
		ExpectationGrammars grammars,
		string snapshotPath,
		string pendingPath)
		: ConstraintResult.WithNotNullValue<string?>(it, grammars),
			IAsyncConstraint<string?>
	{
		private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
		private readonly StringEqualityOptions _options = new StringEqualityOptions("snapshot").IgnoringNewlineStyle();
		private bool _isMatch;
		private string? _pendingError;
		private string? _snapshot;

		public async ValueTask<ConstraintResult> IsMetBy(string? actual, CancellationToken cancellationToken)
		{
			Actual = actual;
			_isMatch = false;
			_pendingError = null;
			_snapshot = null;
			if (actual is null)
			{
				Outcome = Outcome.Failure;
				return this;
			}

			_snapshot = ReadSnapshot();
			if (_snapshot is not null && await _options.AreConsideredEqual(actual, _snapshot))
			{
				if (File.Exists(pendingPath))
				{
					File.Delete(pendingPath);
				}

				_isMatch = true;
				Outcome = Outcome.Success;
				return this;
			}

			WritePending(actual);
			Outcome = _snapshot is null ? Outcome.FailureBothWays : Outcome.Failure;
			return this;
		}

		/// <remarks>
		///     The final line break that <see cref="WritePending(string)" /> adds is removed, which an editor also adds
		///     when it saves a snapshot without one.
		/// </remarks>
		private string? ReadSnapshot()
		{
			if (!File.Exists(snapshotPath))
			{
				return null;
			}

			string snapshot = File.ReadAllText(snapshotPath);
			if (snapshot.EndsWith("\r\n", StringComparison.Ordinal))
			{
				return snapshot.Substring(0, snapshot.Length - 2);
			}

			return snapshot.EndsWith("\n", StringComparison.Ordinal)
				? snapshot.Substring(0, snapshot.Length - 1)
				: snapshot;
		}

		/// <remarks>
		///     A pending file that cannot be written must not hide why the expectation failed, so the error is reported
		///     with the failure instead of being thrown.<br />
		///     The file ends with a line break, so that it stays unchanged when an editor saves it.
		/// </remarks>
		private void WritePending(string actual)
		{
			try
			{
				File.WriteAllText(pendingPath, actual + Environment.NewLine, Utf8WithoutBom);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				_pendingError = exception.Message;
			}
		}

		public override void AppendContexts(ResultContextCollector contexts)
		{
			if (Actual is null || _isMatch)
			{
				return;
			}

			contexts.AddStringDifferencesContext(Actual, _snapshot, _options, Grammars);
			contexts.AddStringContext("Actual", Actual, this);
			contexts.AddStringContext("Snapshot", _snapshot, this);
			contexts.Add(new ResultContext.Fixed("Pending snapshot",
				_pendingError is null ? pendingPath : $"could not be written: {_pendingError}"));
		}

		protected override void AppendNormalExpectation(StringBuilder stringBuilder, string? indentation = null)
			=> stringBuilder.Append(Grammars.Verb("matches", "match")).Append(" the snapshot \"")
				.Append(snapshotPath).Append('"');

		protected override void AppendNormalResult(StringBuilder stringBuilder, string? indentation = null)
		{
			if (_snapshot is null)
			{
				stringBuilder.Append("the snapshot did not exist");
			}
			else
			{
				stringBuilder.Append(_options.GetExtendedFailure(It, Grammars, Actual, _snapshot)
					.Indent(indentation, false));
			}
		}

		protected override void AppendNegatedExpectation(StringBuilder stringBuilder, string? indentation = null)
			=> stringBuilder.Append(Grammars.Verb("does not match", "do not match")).Append(" the snapshot \"")
				.Append(snapshotPath).Append('"');

		protected override void AppendNegatedResult(StringBuilder stringBuilder, string? indentation = null)
		{
			if (_snapshot is null)
			{
				stringBuilder.Append("the snapshot did not exist");
			}
			else
			{
				stringBuilder.Append(It).Append(" did");
			}
		}
	}
}
