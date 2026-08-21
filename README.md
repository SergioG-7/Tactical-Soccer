# ⚽ Tactical Soccer

[Español](#-español) | [English](#-english) | [日本語](#-日本語)

---

## 🇪🇸 Español

**Tactical Soccer** es un prototipo de juego de fútbol táctico con vista cenital donde el jugador dibuja las trayectorias de los futbolistas en tiempo real. Este proyecto demuestra conocimientos avanzados en la creación de mecánicas tácticas, inteligencia artificial (IA) y diseño de interfaces multiplataforma.

### 🎮 Jugar la Demo
* Jugar en el navegador (WebGL) - [https://sergiog-7.itch.io/tactical-soccer] (contraseña = level5)
* Ver gameplay en YouTube - [https://youtu.be/NuzHjXO174s]

### 🛠️ Tecnologías y Herramientas
* **Motor:** Unity (C#)
* **Plataformas:** Android, PC, WebGL
* **Patrones:** State Machine (Máquina de estados), Generación procedural de UI.

### 🚀 Desafíos Técnicos Resueltos (Highlights)
* **Arquitectura de UI Escalable y Multiplataforma:** Sistema de menús adaptado a cualquier resolución panorámica (*Landscape*). Implementación nativa de soporte para el **Safe Area** (Notch de móviles) protegiendo el contenido interactivo y manteniendo los fondos a pantalla completa sin depender de cálculos frágiles.
* **Gestión de Estados e IA en Balón Parado:** Lógica robusta para interrupciones del partido (faltas, saques de banda, córners). Implementación de zonas de exclusión dinámicas: la IA detecta la interrupción, aborta su ruta actual y se desplaza automáticamente para respetar la distancia reglamentaria.
* **Input Táctico y Renderizado de Trayectorias:** Manejo simultáneo de gestos táctiles (Pinch-to-Zoom para la cámara) y dibujo de trayectorias. Uso de `LineRenderer` para trazar órdenes, con limpieza instantánea del buffer visual al pitarse faltas para evitar artefactos en pantalla.

---

## 🇬🇧 English

**Tactical Soccer** is a top-down tactical soccer game prototype where players draw the trajectories of their footballers in real-time. This project showcases advanced skills in gameplay mechanics, artificial intelligence (AI), and cross-platform UI architecture.

### 🎮 Play the Demo
* Play in Browser (WebGL) - [https://sergiog-7.itch.io/tactical-soccer] (password: level5)
* Watch gameplay on YouTube - [https://youtu.be/NuzHjXO174s]

### 🛠️ Technologies & Tools
* **Engine:** Unity (C#)
* **Platforms:** Android, PC, WebGL
* **Patterns:** State Machine, Procedural UI Generation.

### 🚀 Technical Highlights
* **Scalable Cross-Platform UI Architecture:** Menu system tailored for any panoramic resolution (Landscape). Native implementation of **Safe Area** support (mobile notches) to protect interactive content while keeping background images at true full screen without fragile math calculations.
* **State Management & Set-Piece AI:** Robust logic for match interruptions (fouls, throw-ins, corners). Implementation of dynamic exclusion zones: the AI detects the interruption, aborts its current path, and automatically moves back to respect the required regulatory distance.
* **Tactical Input & Path Rendering:** Simultaneous handling of touch gestures (Pinch-to-Zoom for camera) and path drawing. Utilized `LineRenderer` to draw orders, with instant visual buffer cleanup upon fouls to prevent visual artifacts on screen.

---

## 🇯🇵 日本語

**Tactical Soccer（タクティカル・サッカー）**は、プレイヤーがリアルタイムで選手の移動経路を描く、トップダウン型の戦術サッカーゲームのプロトタイプです。このプロジェクトは、ゲームプレイメカニクス、人工知能（AI）、およびクロスプラットフォームUIアーキテクチャの高度な実装スキルを示しています。

### 🎮 デモをプレイする
* ブラウザでプレイ (WebGL) - [https://sergiog-7.itch.io/tactical-soccer] (パスワード：level5)
* YouTubeでゲームプレイを見る - [https://youtu.be/NuzHjXO174s]

### 🛠️ 使用技術とツール
* **エンジン:** Unity (C#)
* **プラットフォーム:** Android, PC, WebGL
* **設計パターン:** ステートマシン (State Machine)、プロシージャルUI生成

### 🚀 主な技術的ハイライト
* **スケーラブルなUIアーキテクチャ:** 任意のランドスケープ（横画面）解像度に対応するUI設計。壊れやすい計算に頼らず、デバイスのノッチ（Safe Area）にネイティブ対応し、背景画像を全画面に維持しつつインタラクティブなUIを保護。
* **ステート管理とセットプレーAI:** ファウル、スローイン、コーナーキックなどの試合中断時の堅牢な進行管理。規定距離（Exclusion Zone）のロジックを実装し、AIが中断を検知すると進行中のルートをキャンセルし、自動的に適切な距離まで後退。
* **戦術的な入力とLineRenderer:** カメラのピンチ操作（Pinch-to-Zoom）と経路描画を同時に制御。`LineRenderer`を使用した経路描画と、ファウル発生時の即時描画キャンセル処理による視覚的バグの防止。
