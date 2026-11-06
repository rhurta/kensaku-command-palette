// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace KensakuCmdPal;

public partial class KensakuCmdPalCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = new();

    public KensakuCmdPalCommandsProvider()
    {
        DisplayName = "Kensaku Command Palette";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new KensakuSearchPage())
            {
                Title = DisplayName,
                Subtitle = "Buscar archivos, contenido y capturas",
            },
        ];

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }
}
