use crate::Route;

#[rustfmt::skip]
pub const ROUTES: &[Route] = &[
    Route {
        name: "check-project",
        program: "powershell",
        args: &["-NoProfile", "-NonInteractive", "-File", "scripts/check-project.ps1"],
    },
    Route {
        name: "check-cli",
        program: "python",
        args: &["-X", "utf8", "scripts/check-cli-contract.py"],
    },
    Route {
        name: "ci",
        program: "python",
        args: &["-X", "utf8", "scripts/check-local-ci.py"],
    },
];
