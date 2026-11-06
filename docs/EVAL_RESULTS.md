Execution provider: DirectML (GPU)  

Mean over all images and backgrounds (lower MAE / higher IoU is better):

| variant | MAE x1000 | boundary MAE x1000 | IoU@0.5 | mean sec/img |
|---|---|---|---|---|
| raw-mask | 11.38 | 79.9 | 0.9858 | 0.08 |
| guided r=1/512 eps=0.0001 | 12.63 | 93.9 | 0.9857 | 0.14 |
| guided r=1/512 eps=0.001 | 13.04 | 99.2 | 0.9857 | 0.14 |
| guided r=1/512 eps=0.01 | 14.19 | 115.1 | 0.9857 | 0.14 |
| guided r=1/512 eps=0.05 | 15.00 | 126.9 | 0.9856 | 0.14 |
| guided r=1/256 eps=0.0001 | 15.21 | 120.1 | 0.9849 | 0.14 |
| guided r=1/256 eps=0.001 | 16.03 | 129.3 | 0.9850 | 0.14 |
| guided r=1/256 eps=0.01 | 18.82 | 163.7 | 0.9849 | 0.14 |
| guided r=1/256 eps=0.05 | 20.99 | 191.9 | 0.9844 | 0.14 |
| guided r=1/96 eps=0.0001 | 25.40 | 177.8 | 0.9807 | 0.14 |
| guided r=1/96 eps=0.001 | 27.07 | 190.0 | 0.9806 | 0.14 |
| guided r=1/96 eps=0.01 | 33.79 | 243.0 | 0.9795 | 0.14 |
| guided r=1/96 eps=0.05 | 39.74 | 290.8 | 0.9770 | 0.14 |
| raw-mask+flip | 12.21 | 78.7 | 0.9874 | 0.17 |

Per image and background (raw-mask):

| image | background | MAE x1000 | boundary MAE x1000 | IoU@0.5 |
|---|---|---|---|---|
| img01.png | flat-gray | 2.43 | 43.8 | 0.9971 |
| img01.png | texture | 16.77 | 84.5 | 0.9956 |
| img01.png | camouflage | 30.95 | 81.6 | 0.9949 |
| img02.png | flat-gray | 9.92 | 96.1 | 0.9787 |
| img02.png | texture | 14.77 | 133.9 | 0.9704 |
| img02.png | camouflage | 15.08 | 134.5 | 0.9677 |
| img03.png | flat-gray | 6.78 | 35.4 | 0.9899 |
| img03.png | texture | 13.91 | 64.9 | 0.9759 |
| img03.png | camouflage | 16.96 | 63.0 | 0.9714 |
| img04.png | flat-gray | 3.52 | 69.3 | 0.9973 |
| img04.png | texture | 4.62 | 93.8 | 0.9968 |
| img04.png | camouflage | 4.62 | 107.6 | 0.9969 |
| img05.png | flat-gray | 9.36 | 49.1 | 0.9829 |
| img05.png | texture | 10.99 | 76.1 | 0.9850 |
| img05.png | camouflage | 10.08 | 65.2 | 0.9861 |
