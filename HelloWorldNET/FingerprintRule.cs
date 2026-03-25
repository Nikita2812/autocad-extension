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
    /// </summary>
    public class FingerprintRule
    {
        public string AssignedName { get; set; }
        public string Description { get; set; }
        public string Department { get; set; }  // e.g., "Mechanical", "Electrical", "Instrumentation", "Piping"
        public GeometryConstraints GeometryMatch { get; set; }

        /// <summary>
        /// Evaluates whether a given geometry tally matches all constraints in this rule.
        /// All constraints that are set (not null) must be satisfied for a match.
        /// </summary>
        public bool IsMatch(GeometryTally tally)
        {
            if (tally == null)
                return false;

            if (GeometryMatch == null)
                return true; // Rule with no constraints matches everything

            // Check circles
            if (GeometryMatch.MinCircles.HasValue && tally.Circles < GeometryMatch.MinCircles.Value)
                return false;
            if (GeometryMatch.MaxCircles.HasValue && tally.Circles > GeometryMatch.MaxCircles.Value)
                return false;

            // Check lines
            if (GeometryMatch.MinLines.HasValue && tally.Lines < GeometryMatch.MinLines.Value)
                return false;
            if (GeometryMatch.MaxLines.HasValue && tally.Lines > GeometryMatch.MaxLines.Value)
                return false;

            // Check polylines
            if (GeometryMatch.MinPolylines.HasValue && tally.Polylines < GeometryMatch.MinPolylines.Value)
                return false;
            if (GeometryMatch.MaxPolylines.HasValue && tally.Polylines > GeometryMatch.MaxPolylines.Value)
                return false;

            // Check arcs
            if (GeometryMatch.MinArcs.HasValue && tally.Arcs < GeometryMatch.MinArcs.Value)
                return false;
            if (GeometryMatch.MaxArcs.HasValue && tally.Arcs > GeometryMatch.MaxArcs.Value)
                return false;

            // Check hatches
            if (GeometryMatch.MinHatches.HasValue && tally.Hatches < GeometryMatch.MinHatches.Value)
                return false;
            if (GeometryMatch.MaxHatches.HasValue && tally.Hatches > GeometryMatch.MaxHatches.Value)
                return false;

            // Check texts
            if (GeometryMatch.MinTexts.HasValue && tally.Texts < GeometryMatch.MinTexts.Value)
                return false;
            if (GeometryMatch.MaxTexts.HasValue && tally.Texts > GeometryMatch.MaxTexts.Value)
                return false;

            // All constraints satisfied
            return true;
        }

        public override string ToString()
        {
            return $"{AssignedName}: {Description}";
        }
    }

    /// <summary>
    /// Defines the geometric constraints for a fingerprint rule.
    /// Each constraint is nullable—unset (null) constraints are unconstrained.
    /// </summary>
    public class GeometryConstraints
    {
        public int? MinCircles { get; set; }
        public int? MaxCircles { get; set; }

        public int? MinLines { get; set; }
        public int? MaxLines { get; set; }

        public int? MinPolylines { get; set; }
        public int? MaxPolylines { get; set; }

        public int? MinArcs { get; set; }
        public int? MaxArcs { get; set; }

        public int? MinHatches { get; set; }
        public int? MaxHatches { get; set; }

        public int? MinTexts { get; set; }
        public int? MaxTexts { get; set; }
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
