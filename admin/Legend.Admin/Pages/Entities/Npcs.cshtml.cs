using Legend.Admin.Services;
using Microsoft.AspNetCore.Authorization;

namespace Legend.Admin.Pages.Entities;

[Authorize(Policy = "AnyStaff")]
public class NpcsModel(VersionPackService packs, JsonEntityStore store, LookThumbResolver thumbs, PackPreference packPref)
    : EntityListModelBase(packs, store, thumbs, packPref)
{
    protected override EntityDescriptor Descriptor => EntityCatalog.Npcs;
}
