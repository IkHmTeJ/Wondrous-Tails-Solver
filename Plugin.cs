using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SamplePlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Wondrous Tails Calculator";

        public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly ICommandManager commandManager;

        private readonly WindowSystem windowSystem;
        private readonly CalculatorWindow calculatorWindow;

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;

            windowSystem = new WindowSystem("WondrousTailsCalculator");
            calculatorWindow = new CalculatorWindow();

            windowSystem.AddWindow(calculatorWindow);

            pluginInterface.UiBuilder.Draw += DrawUI;
            pluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;

            commandManager.AddHandler(
                "/wtcalc",
                new CommandInfo(OnCommand)
                {
                    HelpMessage = "Opens or closes the Wondrous Tails Calculator."
                });

            calculatorWindow.IsOpen = true;
        }

        private void DrawUI() => windowSystem.Draw();

        private void ToggleWindow() => calculatorWindow.IsOpen = !calculatorWindow.IsOpen;

        private void OnCommand(string command, string args) => ToggleWindow();

        public void Dispose()
        {
            pluginInterface.UiBuilder.Draw -= DrawUI;
            pluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
            commandManager.RemoveHandler("/wtcalc");
            windowSystem.RemoveAllWindows();
        }
    }

    public sealed class CalculatorWindow : Window
    {
        private const int GridSize = 4;
        private const int CellCount = 16;
        private const int MaxStickers = 9;

        private readonly bool[] grid = new bool[CellCount];

        private double probability1Line;
        private double probability2Lines;
        private double probability3Lines;

        // Shuffle tracking states
        private int retryPoints = 0;
        private int targetLines = 1; // Default to targeting 1 line

        private readonly Dictionary<int, OddsResult> oddsCache = new();

        private static readonly int[][] WinningLines =
        {
            new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 8, 9, 10, 11 }, new[] { 12, 13, 14, 15 }, // Rows
            new[] { 0, 4, 8, 12 }, new[] { 1, 5, 9, 13 }, new[] { 2, 6, 10, 14 }, new[] { 3, 7, 11, 15 }, // Columns
            new[] { 0, 5, 10, 15 }, new[] { 3, 6, 9, 12 }                                                // Diagonals
        };

        public CalculatorWindow() : base("Wondrous Tails Calculator", ImGuiWindowFlags.AlwaysAutoResize)
        {
            UpdateOdds();
        }

        public override void Draw()
        {
            DrawShuffleTracker();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted("Toggle the stickers currently on your journal:");
            ImGui.Spacing();

            DrawGrid();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawStatistics();
            DrawShuffleRecommendation();

            ImGui.Spacing();

            if (ImGui.Button("Reset Grid", new Vector2(170.0f, 25.0f)))
            {
                ResetGrid();
            }
        }

        private void DrawShuffleTracker()
        {
            ImGui.TextUnformatted("Second Chance Points:");
            ImGui.SameLine();

            // Minimal button counter layout for chance points
            if (ImGui.Button("-") && retryPoints > 0) retryPoints--;
            ImGui.SameLine();
            ImGui.Text($" {retryPoints} / 9 ");
            ImGui.SameLine();
            if (ImGui.Button("+") && retryPoints < 9) retryPoints++;

            ImGui.Spacing();
            ImGui.TextUnformatted("Your Target Goal:");
            ImGui.SameLine();
            ImGui.RadioButton("1 Line", ref targetLines, 1);
            ImGui.SameLine();
            ImGui.RadioButton("2 Lines", ref targetLines, 2);
            ImGui.SameLine();
            ImGui.RadioButton("3 Lines", ref targetLines, 3);
        }

        private void DrawGrid()
        {
            int currentStickers = CountPlacedStickers();

            for (int row = 0; row < GridSize; row++)
            {
                for (int column = 0; column < GridSize; column++)
                {
                    int index = (row * GridSize) + column;
                    ImGui.PushID(index);

                    bool wasSelected = grid[index];

                    if (wasSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.60f, 0.30f, 0.60f, 1.00f));
                    }

                    bool clicked = ImGui.Button(wasSelected ? " X " : " . ", new Vector2(40.0f, 40.0f));

                    if (wasSelected)
                    {
                        ImGui.PopStyleColor();
                    }

                    if (clicked)
                    {
                        if (wasSelected)
                        {
                            grid[index] = false;
                            currentStickers--;
                            UpdateOdds();
                        }
                        else if (currentStickers < MaxStickers)
                        {
                            grid[index] = true;
                            currentStickers++;
                            UpdateOdds();
                        }
                    }

                    ImGui.PopID();

                    if (column < GridSize - 1)
                    {
                        ImGui.SameLine();
                    }
                }
            }
        }

        private void DrawStatistics()
        {
            int currentStickers = CountPlacedStickers();
            ImGui.TextUnformatted($"Stickers Placed: {currentStickers} / {MaxStickers}");
            ImGui.Spacing();

            if (ImGui.BeginTable("##WondrousTailsOddsTable", 2, ImGuiTableFlags.SizingFixedFit))
            {
                DrawOddsRow("1 Line Chance:", probability1Line);
                DrawOddsRow("2 Lines Chance:", probability2Lines);
                DrawOddsRow("3 Lines Chance:", probability3Lines);
                ImGui.EndTable();
            }
        }

        private static void DrawOddsRow(string label, double probability)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(label);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted($"{probability:P2}");
        }

        private void DrawShuffleRecommendation()
        {
            int currentStickers = CountPlacedStickers();
            ImGui.Spacing();

            if (currentStickers != 7)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Shuffle advice is only available at exactly 7 stickers.");
                return;
            }

            if (retryPoints <= 0)
            {
                ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1.0f), "Out of Second Chance Points! Cannot shuffle.");
                return;
            }

            // Average statistical line baseline layout counts at 7 stickers:
            // 1-Line average: ~49.5% | 2-Line average: ~13.1% | 3-Line average: ~1.2%
            double currentOdds = targetLines == 1 ? probability1Line : (targetLines == 2 ? probability2Lines : probability3Lines);
            double averageOdds = targetLines == 1 ? 0.495 : (targetLines == 2 ? 0.131 : 0.012);

            if (currentOdds < averageOdds)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), "Recommendation: SHUFFLE!");
                ImGui.TextWrapped($"Your current layout is below average for {targetLines} Line(s). Spending 1 point to shuffle is statistically worth it.");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.3f, 0.6f, 0.9f, 1.0f), "Recommendation: KEEP");
                ImGui.TextWrapped("Your current sticker placement layout is above average. Keep placing stickers!");
            }
        }

        private void ResetGrid()
        {
            Array.Clear(grid, 0, grid.Length);
            UpdateOdds();
        }

        private int CountPlacedStickers() => grid.Count(isPlaced => isPlaced);

        private int GetCurrentMask()
        {
            int mask = 0;
            for (int i = 0; i < CellCount; i++)
            {
                if (grid[i]) mask |= 1 << i;
            }
            return mask;
        }

        private void UpdateOdds()
        {
            int currentMask = GetCurrentMask();

            if (oddsCache.TryGetValue(currentMask, out OddsResult cached))
            {
                ApplyOdds(cached);
                return;
            }

            int currentStickerCount = CountBits(currentMask);

            if (currentStickerCount > MaxStickers)
            {
                ApplyOdds(OddsResult.Zero);
                return;
            }

            if (currentStickerCount == MaxStickers)
            {
                int completedLines = CountLinesForMask(currentMask);
                var result = new OddsResult(
                    completedLines >= 1 ? 1.0 : 0.0,
                    completedLines >= 2 ? 1.0 : 0.0,
                    completedLines >= 3 ? 1.0 : 0.0);
                oddsCache[currentMask] = result;
                ApplyOdds(result);
                return;
            }

            var emptyCells = new List<int>();
            for (int i = 0; i < CellCount; i++)
            {
                if ((currentMask & (1 << i)) == 0) emptyCells.Add(i);
            }

            int stickersNeeded = MaxStickers - currentStickerCount;
            long totalOutcomes = 0;
            long outcomesWith1Line = 0;
            long outcomesWith2Lines = 0;
            long outcomesWith3Lines = 0;

            EnumerateCompletions(emptyCells, 0, stickersNeeded, currentMask, completedMask =>
            {
                totalOutcomes++;
                int completedLines = CountLinesForMask(completedMask);
                if (completedLines >= 1) outcomesWith1Line++;
                if (completedLines >= 2) outcomesWith2Lines++;
                if (completedLines >= 3) outcomesWith3Lines++;
            });

            OddsResult calculatedOdds = totalOutcomes == 0
                ? OddsResult.Zero
                : new OddsResult((double)outcomesWith1Line / totalOutcomes, (double)outcomesWith2Lines / totalOutcomes, (double)outcomesWith3Lines / totalOutcomes);

            oddsCache[currentMask] = calculatedOdds;
            ApplyOdds(calculatedOdds);
        }

        private static void EnumerateCompletions(IReadOnlyList<int> emptyCells, int startIndex, int stickersRemaining, int currentMask, Action<int> onCompletedBoard)
        {
            if (stickersRemaining == 0)
            {
                onCompletedBoard(currentMask);
                return;
            }

            int cellsAvailable = emptyCells.Count - startIndex;
            if (cellsAvailable < stickersRemaining) return;

            for (int i = startIndex; i < emptyCells.Count; i++)
            {
                if ((emptyCells.Count - i) < stickersRemaining) break;

                EnumerateCompletions(emptyCells, i + 1, stickersRemaining - 1, currentMask | (1 << emptyCells[i]), onCompletedBoard);
            }
        }

        private static int CountLinesForMask(int mask)
        {
            int completedLines = 0;
            foreach (int[] line in WinningLines)
            {
                int lineMask = (1 << line[0]) | (1 << line[1]) | (1 << line[2]) | (1 << line[3]);
                if ((mask & lineMask) == lineMask) completedLines++;
            }
            return completedLines;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        private void ApplyOdds(OddsResult result)
        {
            probability1Line = result.OneLine;
            probability2Lines = result.TwoLines;
            probability3Lines = result.ThreeLines;
        }

        private readonly struct OddsResult
        {
            public static OddsResult Zero => new(0.0, 0.0, 0.0);
            public double OneLine { get; }
            public double TwoLines { get; }
            public double ThreeLines { get; }
            public OddsResult(double oneLine, double twoLines, double threeLines)
            {
                OneLine = oneLine;
                TwoLines = twoLines;
                ThreeLines = threeLines;
            }
        }
    }
}
