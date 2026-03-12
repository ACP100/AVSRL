# Autonomous Vehicle Simulation using Reinforcement Learning

This project was developed as a Minor Project in the 5th Semester of my Bachelor's Degree in Computer Engineering at Khwopa College of Engineering (Affiliated to Tribhuvan University).

The goal was to explore the intersection of Computer Vision and Deep Reinforcement Learning (DRL) to solve the complexities of autonomous navigation in a risk-free simulated environment.
The project 

## Projectt Overview
This project implements a Self-Driving vehicle simulation in Unity where the simulates vehicle learns to navigate a complex road structure using Proximal Policy Optimization (PPO) algorithm.

## Perception & Sensing Systems

1. RayPerception Sensor 3D (LIDAR Emulation)

The simulation utilizes Unity's RayPerceptionSensor3D to emulate the functionality of a LIDAR system.

Spatial Awareness: Multiple rays are cast in a 360-degree arc around the vehicle to detect the distance and tags of surrounding objects.

Obstacle Avoidance: These sensors provide the RL agent with high-fidelity vector observations regarding the proximity of track barriers, other vehicles, and road boundaries.

 2. Vision-Based Lane Detection

A dedicated camera sensor mounted on the vehicle captures the front-facing road view, which is processed via a dedicated Python server to ensure the vehicle remains centered.

Real-Time Processing: The raw frames are streamed via sockets to a Python environment where OpenCV-based algorithms detect lane markings.

Feedback Loop: The vision system outputs a confidence score that informs the RL agent’s steering decisions, mimicking human-like visual navigation.


## Technical Architecture

1. The Perception Pipeline

To handle complex image processing without slowing down the simulation physics, the camera data is sent to a Python server:

Preprocessing: Grayscale conversion and Gaussian Blur to reduce noise.

Canny Edge Detection: Isolating road markings.

Region of Interest (ROI): Masking unnecessary data (like the sky or dashboard).

Hough Line Transform: Extracting the mathematical coordinates of lane lines.

Confidence Score: Calculating the vehicle's alignment relative to the lane center.

2. Reinforcement Learning (PPO)

The agent is trained using Proximal Policy Optimization, chosen for its stability and efficiency.

Reward Function Structure:

- Positive Reward: Maintaining center lane alignment and target velocity.

- Negative Penalty: Colliding with barriers, leaving the road, or remaining stationary (to prevent "safe" but useless behavior).

##  Methodology & Tools

Simulation Engine: Unity (v2021.3 LTS)

RL Framework: Unity ML-Agents

Language: C# (Environment Control) & Python (AI & CV)

Computer Vision: OpenCV

Road Design: RoadArchitec
