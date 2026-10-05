use crate::Route;

#[rustfmt::skip]
pub const ROUTES: &[Route] = &[
    Route {
        name: "check-project",
        program: "powershell",
        args: &["-NoProfile", "-NonInteractive", "-File", "scripts/check-project.ps1"],
    },
];
