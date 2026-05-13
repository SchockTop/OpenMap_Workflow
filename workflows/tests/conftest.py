import sys, os
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))


def pytest_addoption(parser):
    parser.addoption(
        "--render-visuals",
        action="store_true",
        default=False,
        help="Run visual regression renders (needs Blender on PATH)",
    )
