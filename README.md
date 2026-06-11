# 88-ship-hydrodynamics-sandbox

## 船舶水动力学流体推演沙盒

基于 Unity 3D (HLSL Compute Shader) + C# 开发的高保真度船舶水动力学仿真系统。

---

## 核心特性

### 1. 巨型船体实时体素化 (GPU Voxelization)
- 通过 HLSL Compute Shader 在 GPU 中将复杂的 STL 船体网格进行三维体素化
- 基于光线投射法 (Ray-Casting) 进行内外判定，精确识别船体内部体素
- 根据吃水线深度切割，计算每个体素的浸没比例
- GPU 并行归约 (Parallel Reduction) 极速计算：
  - **排水体积** (Displaced Volume)
  - **湿表面积** (Wetted Surface Area)
  - **水线面面积** (Waterplane Area)
  - **浮心位置** (Center of Buoyancy)
  - **稳心高度 GM** (Metacentric Height)

### 2. 浅水方程 (SWE) 波面演化推演
- 超高分辨率高度场纹理 (Heightfield Texture) 表示水面
- 基于二维浅水方程 (Shallow Water Equations) 的物理真实波浪模拟
- 二阶中心有限差分离散化计算水面梯度
- 包含物理项：
  - **重力压力项** - 水面高度梯度驱动流体流动
  - **对流项 (Advection)** - 流体自平流
  - **粘性耗散项** - 真实流体粘性
  - **色散项 (Dispersion)** - 四阶差分模拟波色散
  - **CFL 稳定条件** - 自动时间步长调节

### 3. 船体-水面交互
- **Kelvin 船艏波** - 基于傅汝德数的真实开尔文波系
- **船体排水波浪** - 船舶排开水体产生的波形
- **非线性波-体耦合** - 船体作为动态波源影响水面

### 4. 完整水动力计算
- **浮力 (Buoyancy)** - 阿基米德原理，基于体素化排水体积
- **形状阻力 (Form Drag)** - 基于迎流投影面积
- **摩擦阻力 (Frictional Drag)** - ITTC 1957 摩擦线 + 雷诺数
- **兴波阻力 (Wave Drag)** - 傅汝德数相关的兴波阻力峰/谷
- **附加质量 (Added Mass)** - 加速排水的虚拟质量
- **辐射阻尼 (Radiation Damping)** - 船体运动辐射波的能量耗散
- **横摇/纵摇/首摇阻尼** - 旋转自由度的阻尼力矩
- **波浪激励力 (Wave Excitation)** - 入射波对船体的激励

---

## 目录结构

```
Assets/
├── Shaders/
│   ├── Compute/
│   │   ├── HullVoxelization.compute   # 船体体素化 Compute Shader
│   │   └── SWEWater.compute           # 浅水方程波面演化 Compute Shader
│   └── Water/
│       ├── SWEWaterSurface.shader     # 水面表面渲染 Shader (PBR)
│       ├── VoxelVisualization.shader  # 体素场可视化 Shader (光线步进)
│       └── ShipHullStandard.shader    # 船体标准 PBR Shader
├── Scripts/
│   ├── Core/
│   │   ├── HydroStructs.cs            # 核心数据结构定义
│   │   ├── HydrodynamicsSandbox.cs    # 主沙盒管理器
│   │   ├── ShipController.cs          # 船舶推进与操纵控制器
│   │   └── SceneBootstrap.cs          # 场景自动初始化
│   ├── Voxelization/
│   │   └── HullVoxelizer.cs           # 船体体素化 C# 控制器
│   ├── Water/
│   │   ├── SWEWaterSimulator.cs       # SWE 水面仿真控制器
│   │   └── WaterSurfaceRenderer.cs    # 水面网格渲染器
│   └── HullForces/
│       └── HullHydrodynamics.cs       # 船体浮力与水动力计算
└── Materials/
    ├── M_WaterSurface.mat             # 水面材质
    ├── M_ShipHull.mat                 # 船体材质
    └── M_VoxelVisualization.mat       # 体素可视化材质
```

---

## 快速开始

### 环境要求
- Unity 2022.3 LTS 或更高版本
- HDRP 渲染管线 (推荐)
- 支持 Compute Shader 5.0 的 GPU
- 推荐显存 ≥ 4GB

### 运行步骤
1. 使用 Unity 2022.3+ 打开本项目
2. 创建新场景或使用默认场景
3. 运行场景，系统将自动初始化：
   - 创建水面仿真系统 (512×512 高度场)
   - 生成程序化船体 (参数化船型)
   - 绑定体素化与水动力计算组件
   - 初始化环境波浪

### 操作说明
- **W/S**: 前进/后退 (油门)
- **A/D**: 左右舵
- **空格**: 制动
- **鼠标左键拖拽**: 旋转视角
- **鼠标滚轮**: 缩放
- **鼠标中键拖拽**: 平移视角

---

## 技术实现细节

### Compute Shader 体素化流程
```
ClearVoxelGrid (8×8×8 线程组)
    ↓
VoxelizeHull: 光线-三角形相交 + 有符号距离场
    ↓
ReduceVolume: 组内共享内存归约 (体积/面积/浮心)
    ↓
ReduceArea: 最终全局归约
    ↓
BuildVoxelTexture: 输出 3D 可视化纹理
```

### SWE 数值方法
- **空间离散**: 二阶中心差分
- **时间积分**: 显式欧拉 + CFL 自适应子步
- **边界条件**: 吸收边界层 ( sponge layer )
- **稳定性**: CFL < 0.5 自动调节时间步

---

## 可调参数

### 体素化参数
- `GridSize`: 体素网格分辨率 (默认 64×32×64)
- `CellSize`: 体素边长 (米)
- `UpdateInterval`: 体素化更新频率 (秒)

### 水面仿真参数
- `GridResolution`: 水面高度场分辨率 (默认 512)
- `RestDepth`: 静水深 (默认 10m)
- `Viscosity`: 运动粘度系数
- `Damping`: 全局阻尼
- `SubSteps`: 每帧物理子步数

### 水动力参数
- `WaterDensity`: 海水密度 (默认 1025 kg/m³)
- `FormDragCoefficient`: 形状阻力系数
- `WaveDragCoefficient`: 兴波阻力系数
- `AddedMassCoefficient`: 附加质量系数

---

## License

MIT