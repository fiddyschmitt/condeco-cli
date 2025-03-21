from PyQt5.QtWidgets import QApplication, QWidget, QLabel, QLineEdit, QGridLayout, QPushButton, QVBoxLayout, QCheckBox, QHBoxLayout, QTextEdit
from PyQt5.QtGui import QFont
from PyQt5.QtCore import Qt, QThread, pyqtSignal
import configparser
import subprocess
import os

class ProcessRunner(QThread):
    output_received = pyqtSignal(str)

    def __init__(self, exe_path):
        super().__init__()
        self.exe_path = exe_path

    def run(self):
        process = subprocess.Popen([self.exe_path, "--autobook"], cwd=os.path.dirname(self.exe_path),
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1, universal_newlines=True)
        for line in iter(process.stdout.readline, ''):
            self.output_received.emit(line.strip())
        for line in iter(process.stderr.readline, ''):
            self.output_received.emit(line.strip())
        process.stdout.close()
        process.stderr.close()
        process.wait()

class RoundedEntryUI(QWidget):
    def __init__(self):
        super().__init__()
        self.initUI()

    def initUI(self):
        self.setWindowTitle("Workspace Selector")
        self.setMinimumSize(750, 400)
        self.setStyleSheet("background-color: white; padding: 15px; color: black;")

        main_layout = QVBoxLayout()
        account_layout = QGridLayout()
        form_layout = QGridLayout()
        days_layout = QHBoxLayout()

        account_labels = ["BaseUrl", "Username", "Password"]
        self.account_entries = {}
        for i, label_text in enumerate(account_labels):
            label = QLabel(label_text)
            label.setFont(QFont("Arial", 10))
            label.setStyleSheet("color: black; font-weight: bold;")
            account_layout.addWidget(label, 0, i)
            entry = QLineEdit()
            entry.setFont(QFont("Arial", 10))
            entry.setFixedHeight(30)
            if label_text == "Password":
                entry.setEchoMode(QLineEdit.Password)
            entry.setStyleSheet("QLineEdit { border: 2px solid #D3D3D3; border-radius: 10px; padding: 5px; background-color: white; color: black; } QLineEdit:focus { border-color: #A9A9A9; }")
            account_layout.addWidget(entry, 1, i)
            self.account_entries[label_text] = entry

        labels = ["Country", "Location", "Group", "Floor", "Workspace type", "Desk"]
        self.entries = {}
        for i, label_text in enumerate(labels):
            label = QLabel(label_text)
            label.setFont(QFont("Arial", 10))
            label.setStyleSheet("color: black; font-weight: bold;")
            form_layout.addWidget(label, 0, i)
            entry = QLineEdit()
            entry.setFont(QFont("Arial", 10))
            entry.setFixedHeight(30)
            entry.setStyleSheet("QLineEdit { border: 2px solid #D3D3D3; border-radius: 10px; padding: 5px; background-color: white; color: black; } QLineEdit:focus { border-color: #A9A9A9; }")
            form_layout.addWidget(entry, 1, i)
            self.entries[label_text] = entry

        self.day_checkboxes = {}
        self.selected_days = []
        days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
        for day in days:
            checkbox = QCheckBox(day)
            checkbox.setFont(QFont("Arial", 10))
            checkbox.setStyleSheet("color: black;")
            checkbox.stateChanged.connect(self.update_days)
            self.day_checkboxes[day] = checkbox
            days_layout.addWidget(checkbox)

        main_layout.addLayout(account_layout)
        main_layout.addLayout(form_layout)
        main_layout.addLayout(days_layout)

        button_layout = QVBoxLayout()

        self.commit_button_overwrite = QPushButton("Commit Config (Overwrite)")
        self.commit_button_overwrite.setStyleSheet("""
            QPushButton {
                border: 2px solid #4CAF50;
                border-radius: 10px;
                background-color: #4CAF50;
                color: white;
                padding: 5px;
            }
            QPushButton:hover {
                background-color: #45a049;
            }
        """)
        self.commit_button_overwrite.clicked.connect(lambda: self.export_config(overwrite=True))
        button_layout.addWidget(self.commit_button_overwrite)

        self.commit_button_append = QPushButton("Commit Config (Append)")
        self.commit_button_append.setStyleSheet("""
            QPushButton {
                border: 2px solid #FFA500;
                border-radius: 10px;
                background-color: #FFA500;
                color: white;
                padding: 5px;
            }
            QPushButton:hover {
                background-color: #E69500;
            }
        """)
        self.commit_button_append.clicked.connect(lambda: self.export_config(overwrite=False))
        button_layout.addWidget(self.commit_button_append)

        self.run_existing_button = QPushButton("Run with Existing Config")
        self.run_existing_button.setStyleSheet("""
            QPushButton {
                border: 2px solid #007BFF;
                border-radius: 10px;
                background-color: #007BFF;
                color: white;
                padding: 5px;
            }
            QPushButton:hover {
                background-color: #0056b3;
            }
        """)
        self.run_existing_button.clicked.connect(self.run_existing_config)
        button_layout.addWidget(self.run_existing_button)

        self.delete_config_button = QPushButton("Delete Existing Config")
        self.delete_config_button.setStyleSheet("""
            QPushButton {
                border: 2px solid #FF0000;
                border-radius: 10px;
                background-color: #FF0000;
                color: white;
                padding: 5px;
            }
            QPushButton:hover {
                background-color: #CC0000;
            }
        """)
        self.delete_config_button.clicked.connect(self.delete_config)
        button_layout.addWidget(self.delete_config_button)

        main_layout.addLayout(button_layout)

        self.terminal_output = QTextEdit()
        self.terminal_output.setReadOnly(True)
        self.terminal_output.setStyleSheet("background-color: black; color: white; font-family: monospace;")
        self.terminal_output.setFixedHeight(150)
        main_layout.addWidget(self.terminal_output)

        self.setLayout(main_layout)

    def run_existing_config(self):
        exe_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "condeco-cli.exe")
        self.process_runner = ProcessRunner(exe_path)
        self.process_runner.output_received.connect(self.terminal_output.append)
        self.process_runner.start()

    def delete_config(self):
        config_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.ini")
        if os.path.exists(config_path):
            os.remove(config_path)
            self.terminal_output.append("Config file deleted successfully.")
        else:
            self.terminal_output.append("No config file found to delete.")

    def update_days(self):
        self.selected_days = [day for day, checkbox in self.day_checkboxes.items() if checkbox.isChecked()]

    def export_config(self, overwrite=True):
        if all(not entry.text().strip() for entry in self.account_entries.values()) and \
           all(not entry.text().strip() for entry in self.entries.values()) and \
           not any(checkbox.isChecked() for checkbox in self.day_checkboxes.values()):
            self.terminal_output.append("Please make an entry or load an existing config.")
            return

        config_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.ini")
        self.terminal_output.append(f"Reading config from: {config_path}")

        config = configparser.ConfigParser()
        if not overwrite:
            config.read(config_path)

        config["Account"] = {key: entry.text() for key, entry in self.account_entries.items()}
        config["Book"] = {key.replace(" ", ""): entry.text() for key, entry in self.entries.items()}
        config["Book"]["Days"] = ",".join(self.selected_days) if self.selected_days else ""

        with open(config_path, "w" if overwrite else "a") as configfile:
            config.write(configfile, space_around_delimiters=False)

        self.terminal_output.append("Config file saved successfully.")
        self.run_existing_config()

if __name__ == "__main__":
    import sys
    app = QApplication(sys.argv)
    window = RoundedEntryUI()
    window.show()
    sys.exit(app.exec_())
