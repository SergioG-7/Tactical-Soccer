# ⚽ Tactical Soccer

[Español](#-español) | [English](#-english) | [日本語](#-日本語)

---

## 🇪🇸 Español

**Tactical Soccer** es un juego de fútbol táctico 3D con vista cenital donde el jugador dibuja las trayectorias de los futbolistas en tiempo real. Este proyecto demuestra habilidades en la creación de mecánicas tácticas, inteligencia artificial (IA) y diseño de interfaces multiplataforma.

### 🎮 Jugar la Demo
* Jugar en el navegador (WebGL) - <https://sergiog-7.itch.io/tactical-soccer> (contraseña: level5)
* Ver gameplay en YouTube - <https://youtu.be/NuzHjXO174s>

### 🛠️ Tecnologías y Herramientas
* **Motor:** Unity (C#)
* **Plataformas:** Android, PC (Windows), WebGL
* **Patrones:** State Machine (Máquina de estados), Generación procedural de UI.

### 🚀 Desafíos Técnicos
* **Arquitectura de UI Escalable y Multiplataforma:** Sistema de menús adaptado a cualquier resolución panorámica (*Landscape*). Implementación nativa de soporte para **Safe Area** (evitando solapamientos con el notch en móviles), protegiendo el contenido interactivo y manteniendo los fondos a pantalla completa sin depender de cálculos frágiles.
* **Gestión de Estados e IA en Balón Parado:** Lógica robusta para interrupciones del partido (faltas, saques de banda, córners). Implementación de zonas de exclusión dinámicas: la IA detecta la interrupción, aborta su ruta actual y se desplaza automáticamente para respetar la distancia reglamentaria.
* **Input Táctico y Renderizado de Trayectorias:** Manejo simultáneo de gestos táctiles (zoom mediante pellizco / *Pinch-to-Zoom*) y trazado de rutas. Uso de `LineRenderer` para dibujar órdenes visuales con limpieza inmediata del búfer al pitarse faltas, evitando artefactos o líneas residuales en pantalla.

---

## 🇬🇧 English

**Tactical Soccer** is a top-down 3D tactical soccer game where players draw the trajectories of their footballers in real-time. This project demonstrates solid skills in gameplay mechanics, artificial intelligence (AI), and cross-platform UI architecture.

### 🎮 Play the Demo
* Play in Browser (WebGL) - <https://sergiog-7.itch.io/tactical-soccer> (password: level5)
* Watch gameplay on YouTube - <https://youtu.be/NuzHjXO174s>

### 🛠️ Technologies & Tools
* **Engine:** Unity (C#)
* **Platforms:** Android, PC (Windows), WebGL
* **Patterns:** State Machine, Procedural UI Generation.

### 🚀 Technical Challenges
* **Scalable Cross-Platform UI Architecture:** Menu system tailored for any panoramic resolution (Landscape). Native implementation of **Safe Area** support (mobile notches) to protect interactive content while keeping background images at true full screen without fragile calculations.
* **State Management & Set-Piece AI:** Robust logic for match interruptions (fouls, throw-ins, corners). Implementation of dynamic exclusion zones: the AI detects the interruption, aborts its current path, and automatically moves back to respect the required regulatory distance.
* **Tactical Input & Path Rendering:** Simultaneous handling of touch gestures (Pinch-to-Zoom camera control) and path drawing. Utilized `LineRenderer` to draw orders, with instant visual buffer cleanup upon fouls to prevent visual artifacts on screen.

---

## 🇯🇵 日本語

**Tactical Soccer（タクティカル・サッカー）**は、プレイヤーがリアルタイムで選手の移動経路を描く、トップダウン視点の3D戦術サッカーゲームです。ゲームプレイメカニクス、人工知能（AI）、およびクロスプラットフォームUIアーキテクチャの実装力を示しています。

### 🎮 デモをプレイする
* ブラウザでプレイ (WebGL) - <https://sergiog-7.itch.io/tactical-soccer> (パスワード：level5)
* YouTubeでゲームプレイを見る - <https://youtu.be/NuzHjXO174s>

### 🛠️ 使用技術とツール
* **エンジン:** Unity (C#)
* **プラットフォーム:** Android, PC (Windows), WebGL
* **設計パターン:** ステートマシン (State Machine)、プロシージャルUI生成

### 🚀 技術的課題
* **スケーラブルなクロスプラットフォームUI:** 任意のランドスケープ（横画面）解像度に対応するUI設計。デバイスのノッチ（Safe Area）にネイティブ対応し、壊れやすい計算に頼らず背景画像を全画面に維持しながらインタラクティブなUIを保護。
* **ステート管理とセットプレーAI:** ファウル、スローイン、コーナーキックなどの試合中断時における堅牢な状態管理。規定距離（Exclusion Zone）のロジックを実装し、AIが中断を検知すると進行中のルートを即座に破棄し、自動的に適切な距離まで後退。
* **戦術入力と軌跡レンダリング:** カメラのピンチ操作（Pinch-to-Zoom）と経路描画を同時に制御。`LineRenderer`を使用した経路描画に加え、ファウル発生時の即時描画バッファクリア処理によって画面上の表示バグを防止。
