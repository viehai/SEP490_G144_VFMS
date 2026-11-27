using VFMS.Contracts.Users;
using VFMS.Contracts.Users.Validators;

namespace VFMS.UnitTests;

public class SharedAvatarValidationTests
{
    [Theory]
    [InlineData("/api/trainers/7/avatar", true)]
    [InlineData("https://example.test/portrait.png", true)]
    [InlineData("http://example.test/portrait.png", true)]
    [InlineData("//example.test/api/trainers/7/avatar", false)]
    [InlineData("/api/trainers/../avatar", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Account_profile_can_retain_its_shared_internal_avatar_without_accepting_arbitrary_paths(string url, bool valid)
    {
        var result = new UpdateUserProfileRequestValidator().Validate(new UpdateUserProfileRequest("Test User", "0900000011", url));
        Assert.Equal(valid, result.IsValid);
    }
}
