#ifndef THEMEMANAGER_H
#define THEMEMANAGER_H

#include <QObject>
#include <QWidget>
#include <QPalette>
#include <QString>

class ThemeManager : public QObject
{
    Q_OBJECT

public:
    enum Theme {
        Light,
        Dark,
        System
    };

    static ThemeManager& instance();

    void applyTheme(Theme theme, QWidget* rootWidget);
    void applyTheme(const QString& themeName, QWidget* rootWidget);
    Theme getCurrentTheme() const { return m_currentTheme; }

    static QString getGlobalStyleSheet(Theme theme);
    static QString getChatListStyleSheet(Theme theme);
    static QString getMessageWidgetStyleSheet(bool isMine, Theme theme, const QString& style = "bubbles");
    static QString getDaySeparatorStyleSheet(Theme theme);
    static QString getInputStyleSheet(Theme theme);

private:
    ThemeManager() = default;
    ~ThemeManager() = default;
    ThemeManager(const ThemeManager&) = delete;
    ThemeManager& operator=(const ThemeManager&) = delete;

    Theme m_currentTheme = System;

    QPalette createLightPalette();
    QPalette createDarkPalette();
    QPalette createSystemPalette();

    void applyPalette(QWidget* widget, const QPalette& palette);
};

#endif // THEMEMANAGER_H