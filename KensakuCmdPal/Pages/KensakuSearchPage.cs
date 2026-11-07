using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace KensakuCmdPal;

internal sealed partial class KensakuSearchPage : DynamicListPage
{
    private const int Limit = 30;
    private const int DebounceMs = 300; // a warm search takes ~190 ms

    private CancellationTokenSource? _cts;
    private IListItem[] _items = [];

    public KensakuSearchPage()
    {
        Title = "Kensaku";
        Name = Loc.Get("PageName");
        PlaceholderText = Loc.Get("PagePlaceholder");
        ShowDetails = true;
        Icon = IconHelpers.FromRelativePath("Assets\\Kensaku.png");
    }

    public override IListItem[] GetItems() => _items;

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch == newSearch) return;
        _ = SearchAsync(newSearch);
    }

    private async Task SearchAsync(string query)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            _items = [];
            IsLoading = false;
            RaiseItemsChanged();
            return;
        }

        IsLoading = true;
        try
        {
            await Task.Delay(DebounceMs, ct);
            var res = await KensakuClient.Instance.SearchAsync(query, Limit, ct);
            _items = res.Results.Select(ToItem).ToArray();
        }
        catch (OperationCanceledException)
        {
            return; // a newer search superseded this one
        }
        catch (Exception ex)
        {
            _items =
            [
                new ListItem(new NoOpCommand())
                {
                    Title = Loc.Get("ErrorTitle"),
                    Subtitle = ex.Message,
                },
            ];
        }

        if (ct.IsCancellationRequested) return;
        IsLoading = false;
        RaiseItemsChanged();
    }

    private static IListItem ToItem(Hit h)
    {
        // "g" uses the user's regional format (CurrentCulture), so dates localize automatically.
        var when = DateTimeOffset.FromUnixTimeSeconds(h.Modified).LocalDateTime;
        var dir = Path.GetDirectoryName(h.Path) ?? h.Path;

        var body = $"{(string.IsNullOrWhiteSpace(h.Snippet) ? "" : h.Snippet + "\n\n")}" +
                   $"{h.Path}\n{when:g}  ·  {FormatSize(h.Size)}";

        var more = new List<IContextItem>
        {
            new CommandContextItem(new ShowInFolderCommand(h.Path)),
            new CommandContextItem(new CopyTextCommand(h.Path) { Name = Loc.Get("CopyPath") }),
        };

        return new ListItem(new OpenUrlCommand(h.Path))
        {
            Title = h.Filename,
            Subtitle = dir,
            Tags = [new Tag(Loc.MatchType(h.MatchType))],
            Details = new Details { Title = h.Filename, Body = body },
            MoreCommands = more.ToArray(),
        };
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):0.0} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):0.0} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):0} KB",
        _ => $"{bytes} B",
    };
}

internal sealed partial class ShowInFolderCommand : InvokableCommand
{
    private readonly string _path;

    public ShowInFolderCommand(string path)
    {
        _path = path;
        Name = Loc.Get("ShowInFolder");
    }

    public override ICommandResult Invoke()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_path}\"")
        { UseShellExecute = true });
        return CommandResult.Dismiss();
    }
}
