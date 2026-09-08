using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Domain.Identity;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
