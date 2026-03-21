namespace Haley.Enums {
    /// <summary>
    /// Controls how the search term is matched against vault names.
    /// The term is always normalized (trimmed + lowercased) before comparison.
    /// </summary>
    public enum VaultSearchMode {
        /// <summary>Exact match — vault name must equal the search term.</summary>
        Equals = 0,
        /// <summary>vault name must start with the search term.</summary>
        StartsWith = 1,
        /// <summary>vault name must end with the search term.</summary>
        EndsWith = 2,
        /// <summary>vault name must contain the search term anywhere.</summary>
        Contains = 3,
    }
}
