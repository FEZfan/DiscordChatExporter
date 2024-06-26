﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DiscordChatExporter.Core.Discord.Data;
using DiscordChatExporter.Core.Discord.Data.Embeds;
using DiscordChatExporter.Core.Utils.Extensions;

namespace DiscordChatExporter.Core.Exporting;

internal class PlainTextMessageWriter(Stream stream, ExportContext context)
    : MessageWriter(stream, context)
{
    private readonly TextWriter _writer = new StreamWriter(stream);

    private async ValueTask<string> FormatMarkdownAsync(
        string markdown,
        CancellationToken cancellationToken = default
    ) =>
        Context.Request.ShouldFormatMarkdown
            ? await PlainTextMarkdownVisitor.FormatAsync(Context, markdown, cancellationToken)
            : markdown;

    private async ValueTask WriteMessageHeaderAsync(Message message)
    {
        // ID
        await _writer.WriteAsync($"{message.Id} | ");

        // Author
        if (message.IsSystemNotification)
        {
            await _writer.WriteAsync($"SYSTEM/{message.Author.FullName}:");
        }
        else
        {
            await _writer.WriteAsync($"{message.Author.FullName}:");
        }
        await _writer.WriteAsync(" ");

        // Timestamp
        await _writer.WriteAsync($"[{Context.FormatDate(message.Timestamp)}");

        // Edited Timestamp
        if (message.EditedTimestamp.HasValue)
        {
            await _writer.WriteAsync(
                $" / edited {Context.FormatDate(message.EditedTimestamp.GetValueOrDefault())}"
            );
        }

        // End Timestamp
        await _writer.WriteAsync("] ");

        // Tags
        List<string> tags = new();

        // Whether the message is pinned
        if (message.IsPinned)
            tags.Add("pinned");

        // Whether the message is a reply
        if (message.Kind == MessageKind.Reply)
            if (message.Reference is not null)
                tags.Add($"replying: {message.Reference.MessageId}");
            else
                tags.Add("replying: unknown");

        if (tags.Count > 0)
        {
            await _writer.WriteAsync("(");
            await _writer.WriteAsync(string.Join(", ", tags) + ")");
        }

        await _writer.WriteLineAsync();
    }

    private async ValueTask WriteAttachmentsAsync(
        IReadOnlyList<Attachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        if (!attachments.Any())
            return;

        await _writer.WriteLineAsync("{Attachments}");

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _writer.WriteLineAsync(
                await Context.ResolveAssetUrlAsync(attachment.Url, cancellationToken)
            );
        }

        await _writer.WriteLineAsync();
    }

    private async ValueTask WriteEmbedsAsync(
        IReadOnlyList<Embed> embeds,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var embed in embeds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _writer.WriteLineAsync("{Embed}");

            if (!string.IsNullOrWhiteSpace(embed.Author?.Name))
            {
                await _writer.WriteLineAsync(embed.Author.Name);
            }

            if (!string.IsNullOrWhiteSpace(embed.Url))
            {
                await _writer.WriteLineAsync(embed.Url);
            }

            if (!string.IsNullOrWhiteSpace(embed.Title))
            {
                await _writer.WriteLineAsync(
                    await FormatMarkdownAsync(embed.Title, cancellationToken)
                );
            }

            if (!string.IsNullOrWhiteSpace(embed.Description))
            {
                await _writer.WriteLineAsync(
                    await FormatMarkdownAsync(embed.Description, cancellationToken)
                );
            }

            foreach (var field in embed.Fields)
            {
                if (!string.IsNullOrWhiteSpace(field.Name))
                {
                    await _writer.WriteLineAsync(
                        await FormatMarkdownAsync(field.Name, cancellationToken)
                    );
                }

                if (!string.IsNullOrWhiteSpace(field.Value))
                {
                    await _writer.WriteLineAsync(
                        await FormatMarkdownAsync(field.Value, cancellationToken)
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(embed.Thumbnail?.Url))
            {
                await _writer.WriteLineAsync(
                    await Context.ResolveAssetUrlAsync(
                        embed.Thumbnail.ProxyUrl ?? embed.Thumbnail.Url,
                        cancellationToken
                    )
                );
            }

            foreach (var image in embed.Images)
            {
                if (!string.IsNullOrWhiteSpace(image.Url))
                {
                    await _writer.WriteLineAsync(
                        await Context.ResolveAssetUrlAsync(
                            image.ProxyUrl ?? image.Url,
                            cancellationToken
                        )
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(embed.Footer?.Text))
            {
                await _writer.WriteLineAsync(embed.Footer.Text);
            }

            await _writer.WriteLineAsync();
        }
    }

    private async ValueTask WriteStickersAsync(
        IReadOnlyList<Sticker> stickers,
        CancellationToken cancellationToken = default
    )
    {
        if (!stickers.Any())
            return;

        await _writer.WriteLineAsync("{Stickers}");

        foreach (var sticker in stickers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _writer.WriteLineAsync(
                await Context.ResolveAssetUrlAsync(sticker.SourceUrl, cancellationToken)
            );
        }

        await _writer.WriteLineAsync();
    }

    private async ValueTask WriteReactionsAsync(
        IReadOnlyList<Reaction> reactions,
        CancellationToken cancellationToken = default
    )
    {
        if (!reactions.Any())
            return;

        await _writer.WriteLineAsync("{Reactions}");

        foreach (var (reaction, i) in reactions.WithIndex())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i > 0)
            {
                await _writer.WriteAsync(' ');
            }

            await _writer.WriteAsync(reaction.Emoji.Name);
            if (reaction.Count > 1)
            {
                await _writer.WriteAsync($"{reaction.Count}");
            }
        }

        await _writer.WriteLineAsync();
    }

    public override async ValueTask WritePreambleAsync(
        CancellationToken cancellationToken = default
    )
    {
        DateTime d = DateTime.Now;
        TimeZoneInfo tz = TimeZoneInfo.Local;

        await _writer.WriteLineAsync(new string('=', 62));
        await _writer.WriteLineAsync(
            $"Exported with fez's DiscordChatExporter fork v{Assembly.GetExecutingAssembly().GetName().Version}"
        );
        await _writer.WriteLineAsync($"On {d}");
        await _writer.WriteLineAsync(
            $"In Timezone: {(tz.IsDaylightSavingTime(d) ? tz.DaylightName : tz.StandardName)} / UTC {tz.GetUtcOffset(d)}"
        );
        await _writer.WriteLineAsync("");
        await _writer.WriteLineAsync($"Guild: {Context.Request.Guild.Name}");
        await _writer.WriteLineAsync($"Channel: {Context.Request.Channel.GetHierarchicalName()}");

        if (!string.IsNullOrWhiteSpace(Context.Request.Channel.Topic))
        {
            await _writer.WriteLineAsync($"Topic: {Context.Request.Channel.Topic}");
        }

        if (Context.Request.After is not null)
        {
            await _writer.WriteLineAsync(
                $"After: {Context.FormatDate(Context.Request.After.Value.ToDate())}"
            );
        }

        if (Context.Request.Before is not null)
        {
            await _writer.WriteLineAsync(
                $"Before: {Context.FormatDate(Context.Request.Before.Value.ToDate())}"
            );
        }

        await _writer.WriteLineAsync(new string('=', 62));
        await _writer.WriteLineAsync();
    }

    public override async ValueTask WriteMessageAsync(
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        await base.WriteMessageAsync(message, cancellationToken);

        // Header
        await WriteMessageHeaderAsync(message);

        // Content
        if (message.IsSystemNotification)
        {
            await _writer.WriteLineAsync(message.GetFallbackContent());
            await _writer.WriteLineAsync();
        }
        else
        {
            await _writer.WriteLineAsync(
                await FormatMarkdownAsync(message.Content, cancellationToken)
            );
            await _writer.WriteLineAsync();
        }

        // Attachments, embeds, reactions, etc.
        await WriteAttachmentsAsync(message.Attachments, cancellationToken);
        await WriteEmbedsAsync(message.Embeds, cancellationToken);
        await WriteStickersAsync(message.Stickers, cancellationToken);
        await WriteReactionsAsync(message.Reactions, cancellationToken);

        if (
            message.Attachments != null
            || message.Embeds != null
            || message.Stickers != null
            || message.Reactions != null
        )
            await _writer.WriteLineAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await base.DisposeAsync();
    }
}
