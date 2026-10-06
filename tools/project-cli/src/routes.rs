use crate::Route;

pub const PYTHON: &str = if cfg!(windows) { "python" } else { "python3" };

#[rustfmt::skip]
pub const ROUTES: &[Route] = &[
    Route {
        name: "repair-routes",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/repair-pyrevit-routes.py"],
    },
    Route {
        name: "check-public-ci",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/check-public-ci.py"],
    },
    Route {
        name: "package",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/package-release.py"],
    },
    Route {
        name: "check-release",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/check-release.py"],
    },
    Route {
        name: "install",
        program: "powershell",
        args: &["-NoProfile", "-NonInteractive", "-File", "scripts/install.ps1"],
    },
    Route {
        name: "check-live",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/check-live.py"],
    },
    Route {
        name: "check-project",
        program: "powershell",
        args: &["-NoProfile", "-NonInteractive", "-File", "scripts/check-project.ps1"],
    },
    Route {
        name: "check-cli",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/check-cli-contract.py"],
    },
    Route {
        name: "check-portable",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/check-portable.py"],
    },
    Route {
        name: "ci",
        program: PYTHON,
        args: &["-X", "utf8", "scripts/check-local-ci.py"],
    },
];
