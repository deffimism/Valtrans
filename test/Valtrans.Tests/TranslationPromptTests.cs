using Valtrans.Services;
using Valtrans.Models;
using Xunit;

namespace Valtrans.Tests;

public sealed class TranslationPromptTests
{
    private readonly GlossaryService _glossary = new();
    private readonly AppSettings _settings = new() { Game = "VALORANT", ServerRegion = "JP" };

    [Fact]
    public void Prompt_has_no_unrelated_example_location_or_count()
    {
        var prompt = GameTranslationPrompt.Build("B", "KO", _settings, _glossary);
        Assert.DoesNotContain("heaven", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("two", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Korean", prompt);
        Assert.EndsWith("B", prompt);
    }

    [Theory]
    [InlineData("힐 없어", "EN", "heal")]
    [InlineData("해체 중", "JP", "スパイク解除")]
    [InlineData("ローテしよう", "KO", "로테")]
    [InlineData("이번에는 같이 리테이크하자", "EN", "retake")]
    [InlineData("nt, you'll get it next time", "KO", "좋은 시도")]
    [InlineData("ナイストライ、次はいけるよ", "KO", "좋은 시도")]
    [InlineData("mb, wanna try again?", "KO", "내 실수야")]
    [InlineData("Stop swearing in chat", "KO", "욕설")]
    [InlineData("팬텀 사줄까?", "JP", "ファントム")]
    public void Terminology_is_localized(string source, string target, string term)
        => Assert.Contains(term, _glossary.BuildTranslationTerminology(source, target, _settings));

    [Theory]
    [InlineData("ローテしよう", "딸피")]
    [InlineData("フローについて話そう", "딸피")]
    [InlineData("궁금한 게 있어", "ultimate")]
    [InlineData("I can't play for long", "롱")]
    [InlineData("게시판에서 이야기하자", "match")]
    [InlineData("판단이 어려워", "match")]
    [InlineData("다음 판 풀바이 하자", "match")]
    [InlineData("Windows NT is old", "nice try")]
    [InlineData("I have 512 MB of RAM", "my bad")]
    [InlineData("MB/s means megabytes per second", "my bad")]
    [InlineData("I need to retake the exam", "translates to")]
    [InlineData("I'm retaking a photo", "translates to")]
    [InlineData("They are swearing an oath in court", "swearing")]
    public void Short_aliases_do_not_contaminate_other_words(string source, string unexpected)
        => Assert.DoesNotContain(unexpected, _glossary.BuildTranslationTerminology(source, "EN", _settings));

    [Fact]
    public void Match_counter_does_not_become_a_round()
        => Assert.Contains("match", _glossary.BuildTranslationTerminology("오늘 두 판만 하고 잘래", "EN", _settings));

    [Fact]
    public void Contrasting_match_and_round_keeps_both_units()
    {
        var terms = _glossary.BuildTranslationTerminology("두 판이 아니라 라운드 두 번만 더 하자는 뜻이야", "JP", _settings);
        Assert.Contains("試合", terms);
        Assert.Contains("ラウンド", terms);
    }

    [Theory]
    [InlineData("One second is enough to react")]
    [InlineData("There is one second left")]
    [InlineData("My ping was one second")]
    public void Literal_second_does_not_inject_wait_idiom(string source)
        => Assert.DoesNotContain("잠깐만", _glossary.BuildTranslationTerminology(source, "KO", _settings));

    [Fact]
    public void Conversational_one_second_is_a_wait_request()
        => Assert.Contains("잠깐만", _glossary.BuildTranslationTerminology("One second, my delivery is here", "KO", _settings));

    [Fact]
    public void Tooth_falling_out_is_not_leaving_the_game()
        => Assert.DoesNotContain("leave the game", _glossary.BuildTranslationTerminology("試合中に歯が抜けた", "EN", _settings));

    [Fact]
    public void Site_label_is_explained_as_a_location_not_a_person()
        => Assert.Contains("Map location:", _glossary.BuildTranslationTerminology("I thought there was an enemy B, but it was a teammate", "KO", _settings));

    [Theory]
    [InlineData("Two B main, one has an Op and the other is low", "KO", "B 메인")]
    [InlineData("A 메인에 적 둘", "EN", "A Main")]
    [InlineData("Cヘブン二人", "KO", "C 헤븐")]
    [InlineData("B 사이트로 가라는 뜻 아니야", "EN", "B Site")]
    [InlineData("Bサイトに行く", "EN", "B Site")]
    public void Compound_location_hint_preserves_site_letter(string source, string target, string expected)
        => Assert.Contains(expected, _glossary.BuildTranslationTerminology(source, target, _settings));

    [Theory]
    [InlineData("A short story")]
    [InlineData("A main reason")]
    public void Ordinary_article_and_adjective_are_not_a_compound_site(string source)
        => Assert.DoesNotContain("site and area together", _glossary.BuildRelevantPromptGlossary(source, "KO", _settings));

    [Fact]
    public void Low_definition_is_context_not_a_literal_translation()
    {
        var hint = _glossary.BuildTranslationTerminology("Two B main, the other is low", "KO", _settings);
        Assert.Contains("Context for \"low\"", hint);
        Assert.Contains("Otherwise use the ordinary meaning", hint);
        Assert.DoesNotContain("\"low\" translates to", hint);
    }

    [Fact]
    public void Two_player_descriptions_keep_roles_together()
        => Assert.Contains("distinct weapon and health", GameTranslationPrompt.Build(
            "Two B main, one has an Op and the other is low", "KO", _settings, _glossary));

    [Theory]
    [InlineData("One shop has an Op and the other has low prices")]
    [InlineData("One has an Op and the other has low skill")]
    public void Non_health_low_does_not_inject_player_health(string source)
        => Assert.DoesNotContain("distinct weapon and health", GameTranslationPrompt.Build(source, "KO", _settings, _glossary));

    [Theory]
    [InlineData("걔 풀피야", "EN", "full HP")]
    [InlineData("풀피였어", "EN", "full HP")]
    [InlineData("풀피여도 피킹하지 마", "JP", "体力満タン")]
    [InlineData("フルHPだ", "KO", "체력 가득")]
    public void Full_health_keeps_its_health_meaning(string source, string target, string expected)
        => Assert.Contains(expected, _glossary.BuildTranslationTerminology(source, target, _settings));

    [Fact]
    public void Grass_flute_is_not_full_health()
        => Assert.DoesNotContain("full HP", _glossary.BuildTranslationTerminology("풀피리를 불었어", "EN", _settings));

    [Theory]
    [InlineData("걔 원탭 아니야, 풀피야")]
    [InlineData("He isn't one shot; he's full HP")]
    [InlineData("あいつワンショットじゃない、フルHPだ")]
    [InlineData("Can this gun one shot a player at full HP?")]
    public void Health_and_attack_are_distinguished_without_forcing_either_reading(string source)
    {
        var prompt = GameTranslationPrompt.Build(source, "KO", _settings, _glossary);
        Assert.Contains("Context for one shot:", prompt);
        Assert.DoesNotContain("한 대면 죽음", prompt);
    }

    [Theory]
    [InlineData("걔 풀피야")]
    [InlineData("풀피리를 불었어")]
    public void Unrelated_text_does_not_get_two_health_states_hint(string source)
        => Assert.DoesNotContain("Context for one shot:", GameTranslationPrompt.Build(source, "EN", _settings, _glossary));

    [Theory]
    [InlineData("Press the B key")]
    [InlineData("I got a B on my exam")]
    [InlineData("The player named B said hello")]
    public void Unrelated_single_letters_do_not_become_sites(string source)
        => Assert.DoesNotContain("Map location:", _glossary.BuildTranslationTerminology(source, "KO", _settings));

    [Theory]
    [InlineData("팬텀 사줄까?", "buying for whom")]
    [InlineData("I hit Reyna for 120, not Jett", "not score points")]
    [InlineData("못 들었으면 다시 말해줄게", "hypothetical")]
    [InlineData("적 한 명인 줄 알았는데 팀원이었어", "earlier belief")]
    public void Applicable_fidelity_hints_preserve_semantic_roles(string source, string expected)
        => Assert.Contains(expected, GameTranslationPrompt.Build(source, "EN", _settings, _glossary));

    [Fact]
    public void Greeting_does_not_get_irrelevant_damage_or_purchase_hints()
    {
        var prompt = GameTranslationPrompt.Build("안녕하세요", "EN", _settings, _glossary);
        Assert.DoesNotContain("Damage dealt", prompt);
        Assert.DoesNotContain("buying for whom", prompt);
        // A missing optional hint must not add a new empty prompt line and alter
        // unrelated messages' token sequence.
        Assert.DoesNotContain("Do not invent facts.\n\n\n\n", prompt.Replace("\r", ""));
    }

    [Theory]
    [InlineData("힐은 7초 뒤에 돼", "EN", "Back")]
    [InlineData("체력 12 남았고 힐은 7초 뒤에 돼", "EN", "Back")]
    [InlineData("I have 12 HP left", "KO", "왼쪽")]
    public void Time_and_remaining_health_do_not_inject_directions(string source, string target, string wrong)
        => Assert.DoesNotContain(wrong, _glossary.BuildTranslationTerminology(source, target, _settings));

    [Fact]
    public void Timing_does_not_hide_a_real_direction_in_another_clause()
        => Assert.Contains("Back", _glossary.BuildTranslationTerminology("뒤에 적 있어, 힐은 10초 뒤에 돼", "EN", _settings));

    [Fact]
    public void User_terminology_takes_priority()
    {
        _settings.CustomGlossary["힐"] = "custom-heal";
        var prompt = _glossary.BuildTranslationTerminology("힐 있어?", "EN", _settings);
        Assert.Contains("custom-heal", prompt);
        Assert.DoesNotContain("\"heal\"", prompt);
    }

    [Fact]
    public void Ordinary_chat_keeps_conversation_context()
    {
        var prompt = GameTranslationPrompt.Build("안녕하세요", "EN", _settings, _glossary);
        Assert.Contains("casual chat", prompt);
        Assert.DoesNotContain("concise FPS", prompt);
    }

    [Theory]
    [InlineData("daijoubu shinpai shinaide", "大丈夫、心配しないで")]
    [InlineData("DAIJOUBU, SHINPAI SHINAIDE!", "大丈夫、心配しないで")]
    public void Complete_romaji_reassurance_is_not_split_into_mixed_scripts(string source, string expected)
        => Assert.Equal(expected, ChatTextSanitizer.ConvertCommonRomanizedJapanese(source));
}
