using MediatR;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

public record AddPost(string Title, string Body, byte[]? Image, string? ImageType) : IRequest<int>;
public record UpdatePost(int Id, string Title, string Body, byte[]? NewImage, string? NewImageType) : IRequest;
public record DeletePost(int Id) : IRequest;

public class AddPostHandler(IDbContextFactory<ForumDb> factory, Embedder embedder)
    : IRequestHandler<AddPost, int>
{
    public async Task<int> Handle(AddPost r, CancellationToken ct)
    {
        var post = new Post { Title = r.Title, Body = r.Body, Image = r.Image, ImageType = r.ImageType };

        post.Embeddings.Add(new() { Value = await embedder.Text($"{r.Title}\n{r.Body}") });
        if (r.Image is not null)
            post.Embeddings.Add(new() { Value = await embedder.Image(r.Image) });

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        return post.Id;
    }
}

public class UpdatePostHandler(IDbContextFactory<ForumDb> factory, Embedder embedder)
    : IRequestHandler<UpdatePost>
{
    public async Task Handle(UpdatePost r, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var post = await db.Posts.Include(p => p.Embeddings).FirstAsync(p => p.Id == r.Id, ct);

        post.Title = r.Title;
        post.Body = r.Body;
        if (r.NewImage is not null) { post.Image = r.NewImage; post.ImageType = r.NewImageType; }

        // Simplest possible: throw away the old vectors and re-embed
        post.Embeddings.Clear();
        post.Embeddings.Add(new() { Value = await embedder.Text($"{r.Title}\n{r.Body}") });
        if (post.Image is not null)
            post.Embeddings.Add(new() { Value = await embedder.Image(post.Image) });

        await db.SaveChangesAsync(ct);
    }
}

public class DeletePostHandler(IDbContextFactory<ForumDb> factory) : IRequestHandler<DeletePost>
{
    public async Task Handle(DeletePost r, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Posts.Where(p => p.Id == r.Id).ExecuteDeleteAsync(ct);
    }
}

public record GetPosts : IRequest<List<PostDto>>;
public record SearchPosts(string? Text, byte[]? Image) : IRequest<List<PostDto>>;

public class GetPostsHandler(IDbContextFactory<ForumDb> factory)
    : IRequestHandler<GetPosts, List<PostDto>>
{
    public async Task<List<PostDto>> Handle(GetPosts r, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Posts.OrderByDescending(p => p.Id)
            .Select(p => new PostDto(p.Id, p.Title, p.Body, p.Image != null))
            .ToListAsync(ct);
    }
}

public class SearchPostsHandler(IDbContextFactory<ForumDb> factory, Embedder embedder)
    : IRequestHandler<SearchPosts, List<PostDto>>
{
    public async Task<List<PostDto>> Handle(SearchPosts r, CancellationToken ct)
    {
        var q = r.Image is not null
            ? await embedder.Image(r.Image)
            : await embedder.Text(r.Text!, isQuery: true);

        await using var db = await factory.CreateDbContextAsync(ct);

        var hits = await db.Embeddings
            .OrderBy(e => e.Value.CosineDistance(q))
            .Take(20)
            .Select(e => new PostDto(e.Post.Id, e.Post.Title, e.Post.Body, e.Post.Image != null)
            { Distance = e.Value.CosineDistance(q) })
            .ToListAsync(ct);

        // A post may appear twice (its text and its image); results are already
        // sorted by distance, so the first occurrence is its best match
        return hits.GroupBy(p => p.Id).Select(g => g.First()).Take(10).ToList();
    }
}