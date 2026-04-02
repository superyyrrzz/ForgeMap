namespace ForgeMap;

/// <summary>
/// Specifies how a collection property is updated when <see cref="ForgePropertyAttribute.ExistingTarget"/> is <c>true</c>.
/// </summary>
public enum CollectionUpdateStrategy
{
    /// <summary>Replace the entire collection (default — same as non-ExistingTarget behavior).</summary>
    Replace,

    /// <summary>Add new items from source to existing collection. Existing items unchanged.</summary>
    Add,

    /// <summary>
    /// Match items by key, update existing, add new, remove missing.
    /// Requires <see cref="ForgePropertyAttribute.KeyProperty"/> to be set.
    /// </summary>
    Sync
}
