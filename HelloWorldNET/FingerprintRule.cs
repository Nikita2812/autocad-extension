using System;
using System.Collections.Generic;

namespace HelloWorldNET
{
    /// <summary>
    /// Represents a block classification rule that defines geometric constraints.
    /// Rules are loaded from fingerprints.json and used to match anonymous blocks
    /// to semantic asset types (e.g., INSTRUMENT_BUBBLE, VALVE_GATE, etc.).
    /// Multiple rules can match the same block; all matches are returned separated by " / ".
    /// 
    /// Rules are filtered by department first, then matched against geometry constraints.
    /// This allows different departments to classify the same symbol differently.
    /// 
    /// Matching uses a 3-tier hierarchy:
    /// 1. Tier 1 (Attribute): Use block name/tag if available
    /// 2. Tier 2 (Geometry): Check exact-match constraints (circles, lines, polylines, etc.)
    /// 3. Tier 3 (Aspect Ratio): If multiple rules match Tier 2, use aspect ratio ±20% tolerance
    /// </summary>
    public class FingerprintRule
    {
        public string AssignedName { get; set; }
        public string Description { get; set; }
        public string Department { get; set; }  // e.g., "Mechanical", "Electrical", "Instrumentation", "Piping"
        public GeometryConstraints GeometryMatch { get; set; }

        /// <summary>
        /// Evaluates whether a given geometry tally matches this rule using 3-tier matching:
        /// Tier 2 (Geometry): All set constraints must match exactly.
        /// Tier 3 (Aspect Ratio): If AspectRatioHint is set, validate bounding box aspect ratio ±20%.
        /// </summary>
        public bool IsMatch(GeometryTally tally, decimal? boundingBoxAspectRatio = null)
        {
            if (tally == null)
                return false;

            if (GeometryMatch == null)
                return true; // Rule with no constraints matches everything

            // TIER 2: Check geometry constraints
            if (!MatchesGeometry(tally))
                return false;

            // TIER 3: Check aspect ratio tie-breaker if both rule hint and calculated ratio are available
            if (boundingBoxAspectRatio.HasValue && GeometryMatch.AspectRatioHint.HasValue)
            {
                decimal hintRatio = GeometryMatch.AspectRatioHint.Value;
                decimal tolerance = hintRatio * 0.20m;  // ±20% tolerance
                decimal minRatio = hintRatio - tolerance;
                decimal maxRatio = hintRatio + tolerance;

                decimal calculatedRatio = boundingBoxAspectRatio.Value;

                // Aspect ratio must be within ±20% of hint
                if (calculatedRatio < minRatio || calculatedRatio > maxRatio)
                    return false;
            }

            // All constraints satisfied
            return true;
        }

        /// <summary>
        /// TIER 2 matching: Evaluates geometry constraints exactly.
        /// All set constraints must match; unset (null) constraints accept any count.
        /// </summary>
        private bool MatchesGeometry(GeometryTally tally)
        {
            // Check circles (exact match if set)
            if (GeometryMatch.Circles.HasValue && tally.Circles != GeometryMatch.Circles.Value)
                return false;

            // Check lines (exact match if set)
            if (GeometryMatch.Lines.HasValue && tally.Lines != GeometryMatch.Lines.Value)
                return false;

            // Check polylines (exact match if set)
            if (GeometryMatch.Polylines.HasValue && tally.Polylines != GeometryMatch.Polylines.Value)
                return false;

            // Check arcs (exact match if set)
            if (GeometryMatch.Arcs.HasValue && tally.Arcs != GeometryMatch.Arcs.Value)
                return false;

            // Check hatches (exact match if set)
            if (GeometryMatch.Hatches.HasValue && tally.Hatches != GeometryMatch.Hatches.Value)
                return false;

            // Check texts (exact match if set)
            if (GeometryMatch.Texts.HasValue && tally.Texts != GeometryMatch.Texts.Value)
                return false;

            return true;
        }

        public override string ToString()
        {
            return $"{AssignedName}: {Description}";
        }
    }

    /// <summary>
    /// Defines the geometric constraints for a fingerprint rule.
    /// Each constraint is nullable—unset (null) constraints accept any count.
    /// Set constraints must match exactly.
    /// AspectRatioHint provides collision-resistant disambiguation when multiple rules match.
    /// </summary>
    public class GeometryConstraints
    {
        public int? Circles { get; set; }
        public int? Lines { get; set; }
        public int? Polylines { get; set; }
        public int? Arcs { get; set; }
        public int? Hatches { get; set; }
        public int? Texts { get; set; }
        public decimal? AspectRatioHint { get; set; }  // Width/Height ratio for collision resolution (±20% tolerance)
    }

    /// <summary>
    /// Represents a tally of geometric entities inside an AutoCAD block.
    /// Used to match against FingerprintRule constraints.
    /// </summary>
    public class GeometryTally
    {
        public int Circles { get; set; }
        public int Lines { get; set; }
        public int Polylines { get; set; }
        public int Arcs { get; set; }
        public int Hatches { get; set; }
        public int Texts { get; set; }

        public GeometryTally()
        {
            Circles = 0;
            Lines = 0;
            Polylines = 0;
            Arcs = 0;
            Hatches = 0;
            Texts = 0;
        }

        public override string ToString()
        {
            return $"Circles={Circles}, Lines={Lines}, Polylines={Polylines}, Arcs={Arcs}, Hatches={Hatches}, Texts={Texts}";
        }

        public int TotalGeometry
        {
            get { return Circles + Lines + Polylines + Arcs + Hatches + Texts; }
        }
    }
}
