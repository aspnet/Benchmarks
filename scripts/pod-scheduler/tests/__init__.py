"""Pod-scheduler tests.

Each test module adds the parent ``scripts/pod-scheduler`` directory to
``sys.path`` so it can ``import models`` etc. without requiring the package
to be installed.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_PARENT = os.path.dirname(_HERE)
if _PARENT not in sys.path:
    sys.path.insert(0, _PARENT)