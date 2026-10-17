using System.Collections.Generic;
using System.Globalization;
using aweXpect.Customization;
using aweXpect.Snapshots.Tests.TestHelpers;

namespace aweXpect.Snapshots.Tests;

public sealed partial class ThatSnapshot
{
	public sealed partial class MatchesSnapshot
	{
		public sealed class MemberConverterTests
		{
			private SnapshotId Id { get; } = new($"/src/{Guid.NewGuid():N}/MyTests.cs", "MyTest");

			[Fact]
			public async Task MaskingMemberOf_WithAConverter_ShouldWriteTheConvertedValue()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released, released => released.Year)
						.Masking.MemberOf((RuleSong s) => s.Duration, duration => Math.Round(duration.TotalMinutes));

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = 1969,
				                                             Songs = [
				                                               RuleSong {
				                                                 Duration = 4.0,
				                                                 Title = "Come Together"
				                                               },
				                                               RuleSong {
				                                                 Duration = 3.0,
				                                                 Title = "Something"
				                                               }
				                                             ],
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle();
			}

			[Fact]
			public async Task MaskingMemberOf_WithExplicitTypeArguments_ShouldWriteTheConvertedValue()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf<RuleRelease, DateTime, int>(r => r.Released, released => released.Year)
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = 1969,
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle();
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheConvertedValueIsAStringOrANumber_ShouldWriteItLikeAnyOtherValue()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				CultureInfo previousCulture = CultureInfo.CurrentCulture;
				CultureInfo.CurrentCulture = new CultureInfo("de-DE");
				try
				{
					RuleRelease subject = RuleRelease.AbbeyRoad();

					async Task Act()
						=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
							.Masking.MemberOf((RuleRelease r) => r.Released,
								released => released.ToString("MMMM yyyy", CultureInfo.InvariantCulture))
							.Masking.MemberOf((RuleSong s) => s.Duration, duration => duration.TotalSeconds / 2)
							.Ignoring.Member("Title");

					await That(Act).Throws<XunitException>();
				}
				finally
				{
					CultureInfo.CurrentCulture = previousCulture;
				}

				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = "September 1969",
				                                             Songs = [
				                                               RuleSong {
				                                                 Duration = 129.5
				                                               },
				                                               RuleSong {
				                                                 Duration = 91.0
				                                               }
				                                             ]
				                                           }
				                                           """).IgnoringNewlineStyle()
					.Because("a string is written in quotes and a number in the invariant culture, like any other value");
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheConvertedValueIsAnObject_ShouldIndentItAtTheDepthOfTheMember()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();
				subject.Songs.RemoveAt(1);

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleSong s) => s.Duration, duration => new RuleArtist
						{
							Id = duration.Minutes,
							Name = $"{duration.Seconds} s",
						})
						.Masking.MemberOf((RuleRelease r) => r.Released, released => new[] { released.Year, released.Month, });

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = [
				                                               1969,
				                                               9
				                                             ],
				                                             Songs = [
				                                               RuleSong {
				                                                 Duration = RuleArtist {
				                                                   Id = 4,
				                                                   Name = "19 s"
				                                                 },
				                                                 Title = "Come Together"
				                                               }
				                                             ],
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle();
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheConvertedValueIsAnObject_ShouldNotApplyTheMemberRulesToIt()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released, released => new RuleArtist
						{
							Id = released.Year,
							Name = "The Beatles",
						})
						.Ignoring.Member("Id")
						.Masking.MemberOf((RuleArtist a) => a.Name, name => name.ToUpperInvariant())
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = RuleArtist {
				                                               Id = 1969,
				                                               Name = "The Beatles"
				                                             },
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle()
					.Because("the converter decides what is written, so its result is not converted or left out again");
			}

			[Fact]
			public async Task MaskingMemberOf_ShouldApplyTheTextMaskingsToTheConvertedValue()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released, released => released.Date)
						.Masking.Dates()
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = {Date_1},
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle()
					.Because("the member rules act while the object is written, before the maskings of the text");
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheConverterReturnsNull_ShouldWriteNull()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Title, _ => (string?)null)
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = 1969-09-26T00:00:00.0000000,
				                                             Title = <null>
				                                           }
				                                           """).IgnoringNewlineStyle();
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheValueIsNull_ShouldNotCallTheConverter()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();
				int calls = 0;

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Label, label =>
						{
							calls++;
							return label!.Length;
						})
						.Masking.MemberOf<RuleRelease>(r => r.Label, "{label}")
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(calls).IsEqualTo(0);
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = 1969-09-26T00:00:00.0000000,
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle()
					.Because("a null value is not masked, like by the other maskings");
			}

			[Fact]
			public async Task MaskingMemberOf_ShouldReadTheValueOnce()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				int reads = 0;
				RuleCountingRelease subject = new(() => reads++);

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleCountingRelease r) => r.Released, released => released.Year);

				await That(Act).Throws<XunitException>();
				await That(reads).IsEqualTo(1);
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleCountingRelease {
				                                             Released = 1969
				                                           }
				                                           """).IgnoringNewlineStyle();
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheGetterThrows_ShouldWriteTheExceptionWithoutCallingTheConverter()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleThrowing subject = new();
				int calls = 0;

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleThrowing t) => t.Value, value =>
						{
							calls++;
							return value;
						});

				await That(Act).Throws<XunitException>();
				await That(calls).IsEqualTo(0);
				await That(store.Mismatches[Id]).IsEqualTo("*Value = *boom*").AsWildcard()
					.Because("a value that cannot be read is written with its exception by the formatter");
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheConverterThrows_ShouldThrowTheException()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released,
							int (_) => throw new InvalidOperationException("converter failed"));

				await That(Act).Throws<InvalidOperationException>().WithMessage("converter failed")
					.Because("an exception of the converter is a mistake in the test, which a placeholder would hide");
				await That(store.Mismatches).IsEmpty();
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheValueIsNoValueOfTheMemberType_ShouldNotConvertIt()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleReissue subject = new()
				{
					Released = "Remastered",
					Title = "Abbey Road",
				};

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released, released => released.Year)
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("*Released = \"Remastered\"*").AsWildcard()
					.Because("the reissue hides the release date with a member of another type of the same name");
			}

			[Fact]
			public async Task MaskingMemberOf_WhenAnotherMaskingComesFirst_ShouldUseTheFirstOne()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf<RuleRelease>(r => r.Title, "{title}")
						.Masking.MemberOf((RuleRelease r) => r.Title, title => title.Length)
						.Masking.MemberOf((RuleRelease r) => r.Released, released => released.Year)
						.Masking.Member("Released")
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = 1969,
				                                             Title = {title}
				                                           }
				                                           """).IgnoringNewlineStyle()
					.Because("the first masking that matches a value replaces it");
			}

			[Fact]
			public async Task MaskingMemberOf_WhenTheMemberIsIgnored_ShouldLeaveItOut()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				RuleRelease subject = RuleRelease.AbbeyRoad();
				int calls = 0;

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released, released =>
						{
							calls++;
							return released.Year;
						})
						.Ignoring.MemberOf<RuleRelease>(r => r.Released)
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(calls).IsEqualTo(0);
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Title = "Abbey Road"
				                                           }
				                                           """).IgnoringNewlineStyle()
					.Because("ignoring wins over masking");
			}

			[Fact]
			public async Task WhenGlobalConverters_ShouldApplyThemAfterTheLocalOnes()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				using CustomizationLifetime maskings = Customize.aweXpect.Snapshots().Maskings.Set([
					SnapshotMasking.MemberOf((RuleRelease r) => r.Title, title => title.ToUpperInvariant()),
					SnapshotMasking.MemberOf((RuleRelease r) => r.Released, released => released.Year),
				]);
				RuleRelease subject = RuleRelease.AbbeyRoad();

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Title, title => title.Length)
						.Ignoring.Member("Songs");

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("""
				                                           RuleRelease {
				                                             Label = <null>,
				                                             Released = 1969,
				                                             Title = 10
				                                           }
				                                           """).IgnoringNewlineStyle();
			}

			[Fact]
			public async Task WhenGlobalConverterAndStringSubject_ShouldIgnoreIt()
			{
				InMemorySnapshotStore store = new();
				using CustomizationLifetime _ = Customize.aweXpect.Snapshots().Store.Set(store);
				using CustomizationLifetime maskings = Customize.aweXpect.Snapshots().Maskings.Set([
					SnapshotMasking.MemberOf((RuleRelease r) => r.Title, title => title.Length),
				]);

				async Task Act()
					=> await That("Abbey Road").MatchesSnapshot(Id.SourceFile, Id.MemberName);

				await That(Act).Throws<XunitException>();
				await That(store.Mismatches[Id]).IsEqualTo("Abbey Road");
			}

			[Fact]
			public async Task WhenSubjectIsAString_ShouldThrowInvalidOperationException()
			{
				string subject = "foo";

				async Task Act()
					=> await That(subject).MatchesSnapshot(Id.SourceFile, Id.MemberName)
						.Masking.MemberOf((RuleRelease r) => r.Released, released => released.Year);

				await That(Act).Throws<InvalidOperationException>()
					.WithMessage("A member or type rule only applies to a snapshot of an object*").AsWildcard();
			}

			[Fact]
			public async Task WhenTheMemberIsNested_ShouldThrowArgumentException()
			{
				void Act()
					=> SnapshotMasking.MemberOf((RuleAlbum a) => a.Artist.Name, name => name.Length);

				await That(Act).Throws<ArgumentException>()
					.WithParamName("member").And
					.WithMessage("*accesses a nested member*e.g. 'MemberOf<RuleArtist>(x => x.Name)'.*")
					.AsWildcard();
			}

			[Fact]
			public async Task WhenTheMemberIsNull_ShouldThrowArgumentNullException()
			{
				void Act()
					=> SnapshotMasking.MemberOf<RuleRelease, DateTime, int>(null!, released => released.Year);

				await That(Act).Throws<ArgumentNullException>().WithParamName("member");
			}

			[Fact]
			public async Task WhenTheConverterIsNull_ShouldThrowArgumentNullException()
			{
				void Act()
					=> SnapshotMasking.MemberOf<RuleRelease, DateTime, int>(r => r.Released, null!);

				await That(Act).Throws<ArgumentNullException>().WithParamName("convert");
			}
		}
	}
}

public class RuleRelease
{
	public string? Label { get; set; }
	public DateTime Released { get; set; }
	public List<RuleSong> Songs { get; set; } = [];
	public string Title { get; set; } = "";

	public static RuleRelease AbbeyRoad()
		=> new()
		{
			Released = new DateTime(1969, 9, 26),
			Songs =
			[
				new RuleSong
				{
					Duration = TimeSpan.FromSeconds(259),
					Title = "Come Together",
				},
				new RuleSong
				{
					Duration = TimeSpan.FromSeconds(182),
					Title = "Something",
				},
			],
			Title = "Abbey Road",
		};
}

public sealed class RuleReissue : RuleRelease
{
	public new string Released { get; set; } = "";
}

public sealed class RuleSong
{
	public TimeSpan Duration { get; set; }
	public string Title { get; set; } = "";
}

public sealed class RuleCountingRelease(Action onRead)
{
	public DateTime Released
	{
		get
		{
			onRead();
			return new DateTime(1969, 9, 26);
		}
	}
}
