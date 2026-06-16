#pragma once

#ifndef SMARTTEXTEDIT_H
#define SMARTTEXTEDIT_H

#include <QTextEdit>

class SmartTextEdit : public QTextEdit
{
    Q_OBJECT
    Q_PROPERTY(int maxVisibleLines READ maxVisibleLines WRITE setMaxVisibleLines)

public:
    explicit SmartTextEdit(QWidget* parent = nullptr);

    int maxVisibleLines() const { return m_maxVisibleLines; }
    void setMaxVisibleLines(int lines);
    void updateHeight();

signals:
    void enterPressed(); // Добавить этот сигнал

protected:
    void keyPressEvent(QKeyEvent* event) override;
    void resizeEvent(QResizeEvent* event) override;

private:
    int m_maxVisibleLines = 13;
};

#endif // SMARTTEXTEDIT_H
