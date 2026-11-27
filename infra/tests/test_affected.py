import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "deployment" / "affected.py"
spec = importlib.util.spec_from_file_location("affected", SCRIPT)
affected = importlib.util.module_from_spec(spec)
spec.loader.exec_module(affected)


class AffectedTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()
        for directory in ("App", "Library", "Shared", "Other", "docs", "infra"):
            (self.root / directory).mkdir()
        (self.root / "App" / "App.csproj").write_text(
            '<Project><ItemGroup>'
            '<ProjectReference Include="../Library/Library.csproj" />'
            '<Compile Include="../Shared/Linked.cs" />'
            '</ItemGroup></Project>'
        )
        (self.root / "Library" / "Library.csproj").write_text("<Project />")
        self.project = Path("App/App.csproj")

    def test_project_transitive_and_linked_changes(self):
        for path in ("App/View.cs", "Library/Class.cs", "Shared/Linked.cs"):
            with self.subTest(path=path):
                self.assertTrue(affected.affects(self.root, self.project, [path]))

    def test_unrelated_project_or_docs_do_not_deploy(self):
        for path in ("Other/Class.cs", "docs/guide.md", "infra/main.bicep"):
            with self.subTest(path=path):
                self.assertFalse(affected.affects(self.root, self.project, [path]))

    def test_shared_build_configuration_affects_all(self):
        self.assertTrue(affected.affects(self.root, self.project, ["Directory.Build.props"]))
        self.assertTrue(affected.affects(self.root, self.project, [".dockerignore"]))

    def test_missing_project_reference_fails(self):
        (self.root / "Library" / "Library.csproj").unlink()
        with self.assertRaises(FileNotFoundError):
            affected.affects(self.root, self.project, ["App/View.cs"])


class RepositoryDependencyTests(unittest.TestCase):
    root = Path(__file__).resolve().parents[2]

    def test_staff_app_includes_customer_shared_styles(self):
        project = Path("PD.Mortgages.Staff.Web/PD.Mortgages.Staff.Web.csproj")
        self.assertTrue(affected.affects(self.root, project, ["PD.Mortgages.Customer.Web/wwwroot/css/PDMortgages.css"]))
        self.assertFalse(affected.affects(self.root, project, ["PD.Mortgages.Customer.Web/Program.cs"]))

    def test_shared_telemetry_does_not_affect_bootstrap(self):
        change = ["PD.Shared/RequestTelemetryMiddleware.cs"]
        self.assertTrue(affected.affects(self.root, Path("PD.Products.Api/PD.Products.Api.csproj"), change))
        self.assertFalse(affected.affects(
            self.root, Path("PD.Mortgages.DatabaseBootstrap/PD.Mortgages.DatabaseBootstrap.csproj"), change
        ))


if __name__ == "__main__":
    unittest.main()
