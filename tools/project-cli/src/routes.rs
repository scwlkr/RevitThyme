use crate::Route;

#[rustfmt::skip]
pub const ROUTES: &[Route] = &[
    Route {
        name: "repair-routes",
        program: "python",
        args: &["-X", "utf8", "scripts/repair-pyrevit-routes.py"],
    },
    Route {
        name: "check-public-ci",
        program: "python",
        args: &["-X", "utf8", "scripts/check-public-ci.py"],
    },
    Route {
        name: "package",
        program: "python",
        args: &["-X", "utf8", "scripts/package-release.py"],
    },
    Route {
        name: "check-release",
        program: "python",
        args: &["-X", "utf8", "scripts/check-release.py"],
    },
    Route {
        name: "install",
        program: "powershell",
        args: &["-NoProfile", "-NonInteractive", "-File", "scripts/install.ps1"],
    },
    Route {
        name: "check-live",
        program: "python",
        args: &["-X", "utf8", "scripts/check-live.py"],
    },
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
