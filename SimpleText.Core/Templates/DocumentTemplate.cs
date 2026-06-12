namespace SimpleText.Core.Templates;

public sealed record DocumentTemplate(string Name, string? Mode, string Content);

public static class DocumentTemplates
{
    // --- General Notetaking ---

    private const string NotePlain =
        """
        ================
        Meeting Notes
        ================
        Date:
        Attendees:

        Agenda
        ------
        1.
        2.
        3.

        Notes
        -----


        Action Items
        ------------
        [ ]
        [ ]
        """;

    private const string NoteMarkdown =
        """
        # Meeting Notes

        **Date:**
        **Attendees:**

        ## Agenda

        1.
        2.
        3.

        ## Notes



        ## Action Items

        - [ ]
        - [ ]
        """;

    private const string NoteRst =
        """
        =============
        Meeting Notes
        =============

        :Date:
        :Attendees:

        Agenda
        ======

        1.
        2.
        3.

        Notes
        =====



        Action Items
        ============

        - [ ]
        - [ ]
        """;

    private const string NoteAsciiDoc =
        """
        = Meeting Notes

        Date::
        Attendees::

        == Agenda

        1.
        2.
        3.

        == Notes



        == Action Items

        * [ ]
        * [ ]
        """;

    // --- Technical Report ---

    private const string ReportPlain =
        """
        ================
        Technical Report
        ================
        Title:
        Author:
        Date:
        Version: 1.0

        1. Summary
        ----------


        2. Background
        -------------


        3. Methodology
        --------------


        4. Findings
        -----------


        5. Recommendations
        ------------------


        6. Conclusion
        -------------

        """;

    private const string ReportMarkdown =
        """
        # Technical Report

        | Field   | Value |
        |---------|-------|
        | Title   |       |
        | Author  |       |
        | Date    |       |
        | Version | 1.0   |

        ## 1. Summary



        ## 2. Background



        ## 3. Methodology



        ## 4. Findings



        ## 5. Recommendations



        ## 6. Conclusion

        """;

    private const string ReportRst =
        """
        ================
        Technical Report
        ================

        :Title:
        :Author:
        :Date:
        :Version: 1.0

        1. Summary
        ==========



        2. Background
        =============



        3. Methodology
        ==============



        4. Findings
        ===========



        5. Recommendations
        ==================



        6. Conclusion
        =============

        """;

    private const string ReportAsciiDoc =
        """
        = Technical Report
        :author:
        :revdate:
        :revnumber: 1.0
        :toc:

        == 1. Summary



        == 2. Background



        == 3. Methodology



        == 4. Findings



        == 5. Recommendations



        == 6. Conclusion

        """;

    // --- Development Proposal ---

    private const string ProposalPlain =
        """
        ======================
        Development Proposal
        ======================
        Title:
        Author:
        Date:
        Status: Draft

        1. Overview
        -----------


        2. Problem Statement
        --------------------


        3. Proposed Solution
        --------------------


        4. Alternatives Considered
        --------------------------


        5. Implementation Plan
        ----------------------
        Phase 1:
        Phase 2:
        Phase 3:

        6. Risks
        --------


        7. Timeline
        -----------

        """;

    private const string ProposalMarkdown =
        """
        # Development Proposal

        | Field  | Value |
        |--------|-------|
        | Title  |       |
        | Author |       |
        | Date   |       |
        | Status | Draft |

        ## 1. Overview



        ## 2. Problem Statement



        ## 3. Proposed Solution



        ## 4. Alternatives Considered



        ## 5. Implementation Plan

        - **Phase 1:**
        - **Phase 2:**
        - **Phase 3:**

        ## 6. Risks



        ## 7. Timeline

        """;

    private const string ProposalRst =
        """
        ======================
        Development Proposal
        ======================

        :Title:
        :Author:
        :Date:
        :Status: Draft

        1. Overview
        ===========



        2. Problem Statement
        ====================



        3. Proposed Solution
        ====================



        4. Alternatives Considered
        ==========================



        5. Implementation Plan
        ======================

        - **Phase 1:**
        - **Phase 2:**
        - **Phase 3:**

        6. Risks
        ========



        7. Timeline
        ===========

        """;

    private const string ProposalAsciiDoc =
        """
        = Development Proposal
        :author:
        :revdate:
        :status: Draft
        :toc:

        == 1. Overview



        == 2. Problem Statement



        == 3. Proposed Solution



        == 4. Alternatives Considered



        == 5. Implementation Plan

        Phase 1::

        Phase 2::

        Phase 3::

        == 6. Risks



        == 7. Timeline

        """;

    // --- Software Engineering ---

    private const string ReadmeMarkdown =
        """
        # Project Name

        A short description of what this project does and who it is for.

        ## Features

        -
        -
        -

        ## Getting Started

        ### Installation

        1.
        2.

        ## Usage



        ## Contributing

        -

        ## License

        -
        """;

    private const string ChangelogMarkdown =
        """
        # Changelog

        All notable changes to this project will be documented in this file.

        The format is based on [Keep a Changelog](https://keepachangelog.com/).

        ## [Unreleased]

        ### Added

        -

        ### Changed

        -

        ### Fixed

        -

        ## [1.0.0] - YYYY-MM-DD

        ### Added

        - Initial release.
        """;

    private const string AdrMarkdown =
        """
        # ADR-0001: Title

        ## Status

        Proposed

        ## Context



        ## Decision



        ## Consequences

        """;

    private const string BugReportMarkdown =
        """
        # Bug Report

        ## Summary



        ## Environment

        - **OS:**
        - **Version:**
        - **Build:**

        ## Steps to Reproduce

        1.
        2.
        3.

        ## Expected Behavior



        ## Actual Behavior



        ## Logs / Screenshots



        ## Severity

        Low / Medium / High / Critical
        """;

    private const string PullRequestMarkdown =
        """
        # Pull Request

        ## Summary



        ## Changes

        -
        -

        ## Motivation



        ## Testing



        ## Checklist

        - [ ] Tests pass
        - [ ] Documentation updated
        - [ ] Code reviewed
        """;

    private const string DesignDocMarkdown =
        """
        # Design Doc

        ## Overview



        ## Goals

        -

        ## Non-Goals

        -

        ## Proposed Design



        ## Alternatives Considered



        ## Security / Privacy Considerations



        ## Rollout Plan



        ## Open Questions

        -
        """;

    public static IReadOnlyList<DocumentTemplate> All { get; } = new[]
    {
        new DocumentTemplate("General Notetaking — Plain Text", null, NotePlain),
        new DocumentTemplate("General Notetaking — Markdown", TextModes.Markdown, NoteMarkdown),
        new DocumentTemplate("General Notetaking — reStructuredText", TextModes.ReStructuredText, NoteRst),
        new DocumentTemplate("General Notetaking — AsciiDoc", TextModes.AsciiDoc, NoteAsciiDoc),

        new DocumentTemplate("Technical Report — Plain Text", null, ReportPlain),
        new DocumentTemplate("Technical Report — Markdown", TextModes.Markdown, ReportMarkdown),
        new DocumentTemplate("Technical Report — reStructuredText", TextModes.ReStructuredText, ReportRst),
        new DocumentTemplate("Technical Report — AsciiDoc", TextModes.AsciiDoc, ReportAsciiDoc),

        new DocumentTemplate("Development Proposal — Plain Text", null, ProposalPlain),
        new DocumentTemplate("Development Proposal — Markdown", TextModes.Markdown, ProposalMarkdown),
        new DocumentTemplate("Development Proposal — reStructuredText", TextModes.ReStructuredText, ProposalRst),
        new DocumentTemplate("Development Proposal — AsciiDoc", TextModes.AsciiDoc, ProposalAsciiDoc),

        new DocumentTemplate("Software Engineering — README", TextModes.Markdown, ReadmeMarkdown),
        new DocumentTemplate("Software Engineering — Changelog", TextModes.Markdown, ChangelogMarkdown),
        new DocumentTemplate("Software Engineering — Architecture Decision Record", TextModes.Markdown, AdrMarkdown),
        new DocumentTemplate("Software Engineering — Bug Report", TextModes.Markdown, BugReportMarkdown),
        new DocumentTemplate("Software Engineering — Pull Request", TextModes.Markdown, PullRequestMarkdown),
        new DocumentTemplate("Software Engineering — Design Doc", TextModes.Markdown, DesignDocMarkdown),
    };
}
