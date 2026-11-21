# 타이틀·마을 화면용 그림

생성일: 2026-11-21

내장 `image_gen` 도구로 생성한 P2 프로토타입용 그림이다. 생성 원본을 그대로 프로젝트에 복사했으며 별도의 이미지 편집·리사이즈는 하지 않았다. 기존 게임의 원본 그림은 사용하지 않았다. 그림과 인물 외형은 교체 가능한 시각 예시이며 개별 인물의 확정 기획을 뜻하지 않는다.

## 프로젝트 파일과 배치

| 파일 | 실제 크기 | 용도 |
| --- | --- | --- |
| `Assets/0_Project/2_Town/4_Art/Town.png` | 1672×941 | 타이틀 및 마을 배경 |
| `Assets/0_Project/2_Town/4_Art/Locations.png` | 1672×941 | 2×2 장소 배경 시트 |
| `Assets/0_Project/2_Town/4_Art/Portraits.png` | 1254×1254 | 2×2 인물 초상화 시트 |

위쪽 왼쪽부터 행 순서로 장소는 **주점 / 시장 / 여관 / 길드**, 인물은 **미라 / 오린 / 브란 / 세라**다. Unity 좌표는 왼쪽 아래가 원점이다. 원본 크기를 유지하도록 NPOT Scale은 None을 사용한다.

| 시트 | 인덱스 | Unity Rect (x, y, width, height) |
| --- | --- | --- |
| Locations | 0: 주점 | (0, 471, 836, 470) |
| Locations | 1: 시장 | (836, 471, 836, 470) |
| Locations | 2: 여관 | (0, 0, 836, 470) |
| Locations | 3: 길드 | (836, 0, 836, 470) |
| Portraits | 0: 미라 | (0, 627, 627, 627) |
| Portraits | 1: 오린 | (627, 627, 627, 627) |
| Portraits | 2: 브란 | (0, 0, 627, 627) |
| Portraits | 3: 세라 | (627, 0, 627, 627) |

Locations는 홀수 높이이므로 가운데 한 행(y=470)을 제외한 정수 Rect를 권장한다. 파일 자체를 잘라 저장하지 않고 Unity Sprite Rect로 분리한다.

## 생성 원본과 프롬프트

실행 방식: **내장 image_gen**, 불투명 배경(`transparent_background=false`). CLI/API 키 방식은 사용하지 않았다. 요청 해상도와 실제 출력 해상도는 다르며, 위 표에는 실제 PNG 크기를 기록했다.

### Town

원본 파일: `C:\Users\231108\.codex\generated_images\01a1fbbb-00d3-7b40-8861-06da2559aa6e\exec-ba7db5b0-591c-45c0-89eb-2dc25f0a4e0e.png`

```text
Use case: stylized-concept. Asset type: a single 16:9 landscape background illustration for an original medieval fantasy adventurer-life RPG town hub, 2048x1152. Hand-painted ink and gouache concept art, textured brushwork, grounded architecture, melancholic but welcoming dusk mood. Timber-framed tavern and inn, warm lanterns, small market awnings, a guild hall, worn cobblestone central square, distant hilltop castle. Open neutral central foreground supports UI overlays. Rich amber windows against subdued slate blue and dark earthy brown. Human-scale architecture. Very few tiny distant adult silhouettes, no foreground character. This is background art only: no text, lettering, signs with words, logos, watermarks, borders, panels, buttons, symbols, or UI. Fill the entire wide image.
```

### Locations

원본 파일: `C:\Users\231108\.codex\generated_images\01a1fbbb-00d3-7b40-8861-06da2559aa6e\exec-01c94728-edb2-4449-bd88-209f08cdb35d.png`

```text
Use case: stylized-concept. Asset type: ONE game environment sprite atlas with an EXACT 2 by 2 regular grid, total landscape 2048x1152; each quadrant a landscape 1024x576 vignette. Four completely separate scenes meet at exactly the vertical and horizontal centerlines, no gutters, no border, no text or UI. Original grounded medieval fantasy, hand-painted ink and gouache concept art, textured brushwork, dark earthy brown and slate blue with warm amber lamps. TOP LEFT QUADRANT: cozy tavern interior, heavy wooden tables, bar counter, hearth, mugs, no visible people. TOP RIGHT QUADRANT: open market courtyard, covered wooden stalls with goods and cloth awnings at dusk, no visible people. BOTTOM LEFT QUADRANT: quiet inn guest room, simple bed, shuttered window, candle and travel chest. BOTTOM RIGHT QUADRANT: adventurers guild hall interior, large wooden notice board with blank parchment sheets, stone fireplace, long table. Each scene individually fills its exact rectangular quadrant edge to edge. No captions, letters, logos, watermarks, or decorative separator lines. Keep strong readable space in each scene for later UI overlay.
```

### Portraits

원본 파일: `C:\Users\231108\.codex\generated_images\01a1fbbb-00d3-7b40-8861-06da2559aa6e\exec-e4f1240b-0b5b-4ec3-87ea-7f30636cce0f.png`

```text
Use case: stylized-concept. Asset type: ONE character portrait sprite atlas with EXACT 2 by 2 equal square grid, total square 1536x1536. Each of the four separate portraits fills its 768x768 quadrant edge to edge with no gutter, divider, border, lettering, logos or UI. Original grounded medieval fantasy adventurers, all adults, chest-up portraits centered well inside each quadrant, natural expressions, distinct realistic facial structure, hand-painted ink and gouache concept art, textured brushwork, soft directional lantern light against subdued dark backgrounds. TOP LEFT: MIRA, adult female archer with dark braided hair, green traveling cloak, attentive expression. TOP RIGHT: ORIN, older adult male scholar, silver hair, round spectacles, worn blue robes, thoughtful expression. BOTTOM LEFT: BRAN, adult bearded male fighter, short dark hair, weathered leather armor, calm sturdy expression. BOTTOM RIGHT: SERA, adult female healer with auburn hair, cream hood and cloak, warm expression. No weapons crossing into adjacent quadrants, no face cropped by panel edges. Heads and shoulders must be similar scale. Each portrait composition independent. No names or text.
```

## 확인

세 파일을 직접 확인했다. 장소별 장면과 인물별 얼굴이 지정된 사분면에 있으며 텍스트·버튼·워터마크가 없다. 마을 중앙에는 UI를 올릴 수 있는 광장 공간이 있다. 실제 Windows Player의 화면 캡처 21장을 확인하여 장소별 그림, 네 인물 초상화, UI와 글자 배치를 검토했다. 확인한 화면과 해상도 범위는 [타이틀·마을 검증 기록](../Architecture/TitleAndTown.md#검증)에 있다.

