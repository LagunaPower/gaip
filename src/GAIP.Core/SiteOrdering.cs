namespace GAIP.Core;

public static class SiteOrdering
{
    // Legacy sites keep their alphabetical order; new sites follow the saved order.
    public static IEnumerable<Site> Ordered(IEnumerable<Site> sites) => sites
        .OrderBy(s => s.DisplayOrder is null).ThenBy(s => s.DisplayOrder)
        .ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id);

    public static void Apply(Database db, IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds.Count != db.Sites.Count || orderedIds.Distinct().Count() != orderedIds.Count ||
            !orderedIds.ToHashSet().SetEquals(db.Sites.Select(s => s.Id)))
            throw new ValidationException(["La liste des sites a changé. Rouvrez Configuration pour réorganiser la liste actuelle."]);
        var byId = db.Sites.ToDictionary(s => s.Id);
        for (var i = 0; i < orderedIds.Count; i++) byId[orderedIds[i]].DisplayOrder = i;
    }
}
