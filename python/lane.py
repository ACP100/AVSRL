import cv2
import numpy as np
import socket
import struct
import json
import threading
import time
import os

class LaneDetectionServer:
    def __init__(self, host='127.0.0.1', port=5555):
        self.host = host
        self.port = port

    def start_server(self):
        server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server_socket.bind((self.host, self.port))
        server_socket.listen()

        print(f"Lane detector server running at {self.host}:{self.port}")

        while True:
            conn, addr = server_socket.accept()
            print(f"Connected by {addr}")
            threading.Thread(target=self.handle_client, args=(conn,), daemon=True).start()

    def handle_client(self, conn):
        try:
            while True:
                data_size_bytes = conn.recv(4)
                if not data_size_bytes:
                    print("Client disconnected")
                    break

                data_size = struct.unpack("!I", data_size_bytes)[0]
                print("image recieved")
            
                img_data = b""
                while len(img_data) < data_size:
                    packet = conn.recv(data_size - len(img_data))
                    if not packet:
                        print("Failed to receive full image data")
                        break
                    img_data += packet
                
                if not img_data:
                    print("Received empty image data")
                    continue

               
                # Decode the PNG image
                try:
                    # Use imdecode to decode the PNG image from memory
                    nparr = np.frombuffer(img_data, np.uint8)
                    img = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
                    if img is None:
                        print("Failed to decode image")
                        continue

                    # print(f"Image shape: {img.shape}")

                    # If the image has an alpha channel (RGBA), convert it to BGR
                    if img.shape[2] == 4:
                        img = cv2.cvtColor(img, cv2.COLOR_RGBA2BGR)

                except Exception as e:
                    print(f"Error in image decoding: {e}")
                    continue

                # print(f"Received image data size: {len(img_data)}")

                if img is not None:
                    print(f"Image shape: {img.shape}")
                    confidence = self.detect_lane(img)
                    message = "Lane detection complete"
                    message_bytes = message.encode('utf-8')
                
                # Send the length of the message first (4 bytes)
                    conn.sendall(struct.pack("!I", len(message_bytes)))
                    confidence_actual = struct.pack("!f", confidence)

  
                # Send the actual message
                    conn.sendall(message_bytes) 
                    conn.sendall(confidence_actual)

                    # response = json.dumps({"lane_confidence": confidence}).encode()
                    # conn.sendall(struct.pack("!I", len(response)) + response)

        except Exception as e:
            print(f"Error in lane detection: {e}")
        finally:
            conn.close()

    def calculate_confidence(self, image_width, lane_midpoint):
        image_center = image_width // 2
        deviation = abs(image_center - lane_midpoint)
        max_deviation = image_width // 2  # Worst case: touching image boundary
        confidence = max(0, 1 - (deviation / max_deviation))  # Normalize between 0 and 1
        return confidence
     
    def detect_lane(self, img):
        image_counter = 0
        org = img.copy()
        try:

            # Convert to grayscale
            gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
            # Apply Gaussian blur to reduce noise
            blur = cv2.GaussianBlur(gray, (5, 5), 0)
            # Detect edges using Canny edge detection
            edges = cv2.Canny(blur, 30, 150)
            # Create a mask for the region of interest (ROI)
            height, width = edges.shape
            mask = np.zeros_like(edges)
            polygon = np.array([[
                (0, height), 
                (width, height), 
                (width, int(height * 0.3)), 
                (0, int(height * 0.3))
            ]], np.int32)
            cv2.fillPoly(mask, polygon, 255)
            # Apply the mask to the edges
            masked_edges = cv2.bitwise_and(edges, mask)
            # Apply dilation to strengthen lane markings
            kernel = np.ones((3, 3), np.uint8)
            dilated = cv2.dilate(masked_edges, kernel, iterations=1)
            # Detect lines using Hough Transform
            lines = cv2.HoughLinesP(
                dilated, 
                1, 
                np.pi/180, 
                60, 
                minLineLength=50, 
                maxLineGap=100
                )
            # Initialize confidence and lane detection logic
            lane_midpoints = []
            left_lane = []
            right_lane = []
            if lines is not None:
                
                for line in lines:
                    x1, y1, x2, y2 = line[0]
                    if y2 - y1 == 0:
                        continue
                    slope = (y2 - y1) / (x2 - x1) if x2 != x1 else 999
                    
                    # Categorize lines as left or right based on slope
                    if abs(slope) > 0.5:  # Ignore lines that are too horizontal
                        cv2.line(img, (x1, y1), (x2, y2), (0, 255, 0), 2)
                        midpoint = (x1 + x2) // 2
                        lane_midpoints.append(midpoint)
                        # print('s')
                        if slope < -0.5:  # Left lane
                            left_lane.append((x1, y1))
                            left_lane.append((x2, y2))
                            # print('a')
                        elif slope > 0.5:  # Right lane
                            right_lane.append((x1, y1))
                            right_lane.append((x2, y2))
                            # print('v')
            left_lane = sorted(left_lane, key=lambda point: point[1], reverse=True)
            right_lane = sorted(right_lane, key=lambda point: point[1], reverse=True)
        


            if lane_midpoints:
                avg_lane_midpoint = int(np.mean(lane_midpoints))  # Average midpoint of detected lanes
                confidence = self.calculate_confidence(width, avg_lane_midpoint)
                print(f"Lane detection confidence: {confidence:.2f}")
            else:
                confidence = 0  # No lanes detected, confidence is zero
                print("Lane detection confidence: 0.00")

            image_save_path = os.path.join(os.path.dirname(__file__), "output")
            os.makedirs(image_save_path, exist_ok=True)
            # Display the result for debugging purposes (optional)
            timestamp = time.strftime("%Y%m%d-%H%M%S")
            filename = f"{image_save_path}/{timestamp}_{image_counter}_{confidence}.png"
            if lines is not None:
                for line in lines:
                    x1, y1, x2, y2 = line[0]
                    cv2.line(img, (x1, y1), (x2, y2), (0, 255, 0), 2)

                if left_lane and right_lane:
                # Form a polygon using left and right lane edges
                    lane_polygon = np.array(left_lane + right_lane[::-1], np.int32).reshape((-1, 1, 2))
                # Create overlay and fill polygon with transparency
                    overlay = img.copy()
                    cv2.fillPoly(overlay, [lane_polygon], (255, 0, 0))
                    img = cv2.addWeighted(overlay, 0.4, img, 0.6, 0)  # Adjust transparency
                    cv2.putText(img, f"Confidence: {confidence:.2f}", (70, 70), cv2.FONT_HERSHEY_SIMPLEX,0.51, (255, 0, 0), 2)
                    
            cv2.imwrite(f"{image_save_path}/{timestamp}_{image_counter}.png", org)
            cv2.imwrite(filename, img)
            print(f"Saved lane-detected image: {filename}")
            
            image_counter += 1
            return confidence
        
        except Exception as e:
            print(f"Error in lane detection: {e}")
            return 0.0

if __name__ == "__main__":
    server = LaneDetectionServer()
    server.start_server()



        

            

