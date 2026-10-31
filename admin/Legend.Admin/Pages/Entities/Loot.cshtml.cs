using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;

namespace Legend.Admin.Pages.Entities;

[Authorize(Policy = "AnyStaff")]
public class LootModel(VersionPackService packs, JsonEntityStore store, LookThumbResolver thumbs)
    : EntityListModelBase(packs, store, thumbs)
{
    protected override EntityDescriptor Descriptor => EntityCatalog.Loot;
}
