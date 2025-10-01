using Crucible.Common.Utilities.Services;

namespace Crucible.Common.Tests.Utilities;

public class SlugServiceTests
{
    [Fact]
    public void Slug_WithNoSluggableChars_ReturnsIdentical()
    {
        // given a string that has no sluggable characters
        var input = "hello";

        // when slugged
        var sut = new SlugService();
        var result = sut.Get(input);

        // then the result should be identical to the input
        Assert.Equal(input, result);
    }

    [Fact]
    public void Slug_WithSingleSluggableCharacter_ReturnsExpected()
    {
        // given a string with a single sluggable character in the middle
        var input = "hi friend";

        // when slugged
        var sut = new SlugService();
        var result = sut.Get(input);

        // then the result should be what we expect
        Assert.Equal("hi-friend", result);
    }

    [Fact]
    public void Slug_WithUppercaseCharacters_ReturnsLowercase()
    {
        // given an input with uppercase characters
        var input = "Hi-FrienD";

        // when slugged
        var sut = new SlugService();
        var result = sut.Get(input);

        // then the result should be in lowercase
        Assert.Equal("hi-friend", result);
    }

    [Fact]
    public void Slug_WithMultipleWhitespaceCharacters_DoesntDuplicateSlugCharacter()
    {
        // given an input with multiple consecutive whitespace characters
        var input = "hi       friend";

        // when slugged
        var sut = new SlugService();
        var result = sut.Get(input);

        // then the result should not have consecutive slug characters (dashes)
        Assert.Equal("hi-friend", result);
    }

    [Fact]
    public void Slug_WithWhitespaceAtEnd_DoesntAddSlugCharacterToEnd()
    {
        // given an input with whitespace at the end
        var input = "hi friend     ";

        // when slugged
        var sut = new SlugService();
        var result = sut.Get(input);

        // then the result doesn't have slug characters at the end
        Assert.Equal("hi-friend", result);
    }

    [Fact]
    public void Slug_WithConsecutiveSlugCharactersInput_ReturnsExpected()
    {
        // given an input with multiple consecutive slug characters (dashes)
        var input = "hi--friend";

        // when slugged
        var sut = new SlugService();
        var result = sut.Get(input);

        // then the result is what we expect
        Assert.Equal("hi-friend", result);
    }
}
