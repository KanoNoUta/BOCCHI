using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ClientLanguage = Dalamud.Game.ClientLanguage;

namespace BOCCHI.Modules.Exp;

public class ExpTracker
{
    private float exp = 0f;

    private readonly string pattern;

    private DateTime startTime = DateTime.UtcNow;

    public ExpTracker()
    {
        Reset();
        pattern = getExpMessagePattern(Svc.ClientState.ClientLanguage);
    }

    public void OnTerritoryChanged(uint _)
    {
        Reset();
    }

    public void OnChatMessage(XivChatType type, int timestamp, SeString sender, SeString message, bool isHandled)
    {
        var match = Regex.Match(message.TextValue, pattern);
        if (match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount))
        {
            exp += amount;
        }
    }

    public void Reset()
    {
        exp = 0f;
        startTime = DateTime.UtcNow;
    }

    public float GetExpPerHour()
    {
        var elapsed = (float)(DateTime.UtcNow - startTime).TotalHours;
        if (elapsed <= 0)
        {
            return 0;
        }

        return exp / elapsed;
    }

    private string getExpMessagePattern(ClientLanguage clientLanguage)
    {
        return clientLanguage switch
        {
            ClientLanguage.French => @"Vous gagnez (\d+) points d'expérience de soutien en .+? fantôme",
            ClientLanguage.German => @"Du erhältst (\d+) Phantomroutine als Phantom",
            ClientLanguage.Japanese => @".+?」に(\d+)ポイントのサポート経験値を得た。",
            ClientLanguage.ChineseSimplified => @"获得了(\d[\d,]*)(?:\(\+\d+%\))?点辅助经验值。",
            _ => @"You gain (\d+) Phantom .+? experience points\.",
        };
    }
}
