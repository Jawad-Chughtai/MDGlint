# Field notes

A short guide to the weekend birding trip. Everything here renders the way GitHub shows it.

> [!TIP]
> Bring binoculars and a notebook. The morning walk starts at **6:30** sharp.

## Sightings so far

| Species | Where | Count |
|---|---|---:|
| Grey heron | Reed lake | 3 |
| Kingfisher | River bend | 1 |
| Great tit | Forest edge | 12 |

## Tally script

```python
from collections import Counter

def tally(sightings):
    """Count each species seen on the trip."""
    return Counter(s.species for s in sightings if s.confirmed)
```

## Checklist

- [x] Book the cabin
- [x] Print the trail map
- [ ] Charge the camera
- [ ] Pack lunch for Saturday

<details>
<summary>Packing list</summary>

Rain jacket, field guide, flask, spare batteries.

</details>

Edit this file and save: the preview follows. Keys like <kbd>Ctrl</kbd>+<kbd>S</kbd> render too.
