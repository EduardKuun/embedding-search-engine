using Microsoft.EntityFrameworkCore;
using Pgvector;

public class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public byte[]? Image { get; set; }
    public string? ImageType { get; set; }
    public List<PostEmbedding> Embeddings { get; set; } = [];
}

public class PostEmbedding
{
    public int Id { get; set; }
    public Post Post { get; set; } = null!;
    public Vector Value { get; set; } = null!;
}

public record PostDto(int Id, string Title, string Body, bool HasImage)
{
    public double? Distance { get; init; }
}

public class ForumDb(DbContextOptions<ForumDb> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostEmbedding> Embeddings => Set<PostEmbedding>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector");
        // embeddinggemma2 stores multimodal within the same vector space, 
        // so we can use a single column for both text and image embeddings
        b.Entity<PostEmbedding>().Property(e => e.Value).HasColumnType("vector(768)");

        // HNSW index for approximate nearest-neighbour search using cosine distance.
        b.Entity<PostEmbedding>().HasIndex(e => e.Value)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("m", 16)
            .HasStorageParameter("ef_construction", 64);
    }
}