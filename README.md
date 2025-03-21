# condeco-cli

Access Condeco via a command line interface.

Please leave a ⭐ if you find this useful!

---

## 📦 Download

Portable executables for **Windows**, **Linux**, and **Mac** can be found in the [Releases](https://github.com/fiddyschmitt/condeco-cli/releases/latest) section.

---

## ⚙️ Usage

### 📋 Initial Setup

Run the following command to generate a fresh `config.ini` file:

```
condeco-cli.exe
```

Then, manually populate it with your account and booking details from Condeco.

Example interface:



---

### ✅ Booking with Config

To automatically book all the items defined in the `config.ini` file, run:

```
condeco-cli.exe --autobook
```

Live example output:



---

## 🖥️ GUI Configuration Tool (Windows)

A PyQt5-based GUI configuration tool (`ConfigGUI.py`) is included to simplify creating and managing the `config.ini` file.

### Features:

- Form-based GUI with clear, labeled fields
- Support for all config fields: BaseUrl, Username, Password, Country, Location, Group, Floor, Workspace type, Desk, and Days
- Obfuscated password input
- Day checkboxes to control booking schedules
- Buttons to:
  - Commit config (overwrite or append to existing config.ini)
  - Run `condeco-cli.exe --autobook`
  - Delete existing config
- Embedded terminal window for real-time `condeco-cli.exe` output
- Fully styled with rounded, color-coded buttons matching function

### Launch Instructions

#### Python Requirements

To install the required Python dependencies for the GUI, run:

```bash
pip install -r requirements.txt
```

`requirements.txt` should contain:

```
PyQt5>=5.15
```

1. Ensure Python 3 and PyQt5 are installed.
2. Place `ConfigGUI.py` in the same folder as `condeco-cli.exe`
3. Run:

```
python ConfigGUI.py
```

> 💡 Note: Any config actions or CLI execution will occur relative to this shared directory.

---

## 📅 Scheduling

- **Linux** users: See the [Linux scheduling guide](https://github.com/fiddyschmitt/condeco-cli/wiki/Scheduling-in-Linux)
- **Windows** users: See the [Windows scheduling guide](https://github.com/fiddyschmitt/condeco-cli/wiki/Scheduling-in-Windows)

---

## 🙏 Thanks

Thanks to everyone who's contributed, tested, and supported this project. Your feedback helps make this tool better!

<a href="https://www.buymeacoffee.com/fidel248" target="_blank">
  <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;">
</a>

