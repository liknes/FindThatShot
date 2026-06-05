// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
namespace VideoArchiveManager.Core.Services.Ai;

// A candidate tag plus the CLIP text prompts that describe it. Several prompts
// per label are averaged ("prompt ensembling") into one text embedding, which
// noticeably improves zero-shot accuracy over a single bare word.
public sealed record AiLabel(string TagName, IReadOnlyList<string> Prompts);

// The default scene / subject vocabulary scored against every clip. Mirrors the
// roadmap's examples (sea, fog, ships, birds, beach, forest, city, snow, …) and
// is deliberately drone / landscape / stock-footage oriented. The list is the
// only place new auto-tag candidates need to be added.
public static class AiLabelVocabulary
{
    private static readonly string[] Templates =
    {
        "a photo of {0}",
        "a video frame showing {0}",
        "aerial drone footage of {0}",
        "a scene with {0}"
    };

    public static IReadOnlyList<AiLabel> Default { get; } = Build();

    private static IReadOnlyList<AiLabel> Build()
    {
        // tagName -> the noun phrase plugged into the templates.
        var entries = new (string Tag, string Subject)[]
        {
            ("sea", "the sea"),
            ("ocean waves", "ocean waves"),
            ("beach", "a beach"),
            ("lake", "a lake"),
            ("river", "a river"),
            ("waterfall", "a waterfall"),
            ("fog", "fog and mist"),
            ("clouds", "clouds"),
            ("sunset", "a sunset"),
            ("sunrise", "a sunrise"),
            ("night", "a night scene"),
            ("snow", "snow"),
            ("mountains", "mountains"),
            ("forest", "a forest"),
            ("desert", "a desert"),
            ("field", "an open field"),
            ("city", "a city skyline"),
            ("buildings", "buildings"),
            ("road", "a road"),
            ("bridge", "a bridge"),
            ("harbor", "a harbor"),
            ("ships", "ships and boats"),
            ("cars", "cars and traffic"),
            ("train", "a train"),
            ("aircraft", "an airplane"),
            ("people", "people"),
            ("crowd", "a crowd of people"),
            ("birds", "birds"),
            ("animals", "wild animals"),
            ("flowers", "flowers"),
            ("rain", "rain"),
            ("storm", "a storm"),
            ("rainbow", "a rainbow"),
            ("waterfront", "a waterfront"),
            ("island", "an island"),
            ("park", "a park"),
            ("farm", "farmland"),
            ("industrial", "an industrial site"),
            ("stadium", "a stadium"),
            ("market", "a street market"),

            // Coastal / drone / aerial-oriented additions.
            ("coastline", "a coastline seen from above"),
            ("cliffs", "coastal cliffs"),
            ("bay", "a bay or cove"),
            ("lagoon", "a lagoon"),
            ("peninsula", "a peninsula"),
            ("reef", "a coral reef"),
            ("sandbar", "a sandbar or shoal"),
            ("marina", "a marina full of boats"),
            ("pier", "a pier or jetty"),
            ("lighthouse", "a lighthouse"),
            ("port", "a shipping port with containers"),
            ("kayak", "a kayak or canoe"),
            ("surfer", "a surfer riding a wave"),
            ("swimming pool", "a swimming pool"),
            ("palm trees", "palm trees"),
            ("tropical", "a tropical landscape"),

            // Landscape / terrain.
            ("valley", "a valley"),
            ("canyon", "a canyon or gorge"),
            ("hills", "rolling hills"),
            ("glacier", "a glacier"),
            ("volcano", "a volcano"),
            ("countryside", "the countryside"),
            ("vineyard", "a vineyard"),

            // Urban / man-made.
            ("skyscrapers", "skyscrapers"),
            ("suburb", "a suburban neighborhood"),
            ("highway", "a highway interchange"),
            ("parking lot", "a parking lot"),
            ("construction", "a construction site"),
            ("solar panels", "a solar panel farm"),
            ("wind turbines", "wind turbines"),
            ("sports field", "a sports field"),
            ("fireworks", "fireworks at night")
        };

        var labels = new List<AiLabel>(entries.Length);
        foreach (var (tag, subject) in entries)
        {
            var prompts = Templates.Select(t => string.Format(t, subject)).ToArray();
            labels.Add(new AiLabel(tag, prompts));
        }
        return labels;
    }
}
